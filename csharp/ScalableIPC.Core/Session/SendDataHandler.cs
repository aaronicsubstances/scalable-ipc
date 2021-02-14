using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private ProtocolDatagramFragmenter _datagramFragmenter;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private ISendHandlerAssistant _sendWindowHandler;
        private bool _skipDataExchangeRestrictions;

        public SendDataHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }
        
        public List<ProtocolDatagram> CurrentWindowGroup { get; private set; }
        public int SentDatagramCountInCurrentWindowGroup { get; private set; }

        public bool SendInProgress { get; set; }

        public void Dispose(ProtocolOperationException cause)
        {
            _sendWindowHandler?.Cancel();
            _sendWindowHandler = null;
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                _pendingPromiseCallback.CompleteExceptionally(cause);
                _pendingPromiseCallback = null;
            }
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeDataAck)
            {
                return false;
            }

            // to prevent clashes with other handlers performing sends, 
            // check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sendWindowHandler.OnAckReceived(datagram);
            return true;
        }

        public void ProcessSend(ProtocolMessage message,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            // ensure minimum of 512 and maximum = datagram max length
            int mtu = Math.Min(Math.Max(ProtocolDatagram.MinimumTransferUnitSize, 
                _sessionHandler.NetworkApi.MaximumTransferUnitSize), ProtocolDatagram.MaxDatagramSize);
            _datagramFragmenter = new ProtocolDatagramFragmenter(message, mtu, null);

            // reset fields used for continuation.
            SentDatagramCountInCurrentWindowGroup = 0;

            _skipDataExchangeRestrictions = false;
            if (message.Attributes != null && message.Attributes.ContainsKey(
                ProtocolDatagramOptions.OptionNameSkipDataExchangeProhibitionsInOpeningState))
            {
                string lastValStr = message.Attributes[
                    ProtocolDatagramOptions.OptionNameSkipDataExchangeProhibitionsInOpeningState].LastOrDefault();
                if (lastValStr != null)
                {
                    _skipDataExchangeRestrictions = ProtocolDatagramOptions.ParseOptionAsBoolean(lastValStr);
                }
            }
            if (_sessionHandler.State == SessionState.Opening)
            {
                if (_skipDataExchangeRestrictions)
                {
                    TransitionToOpenState();
                }
                else
                {
                    _sessionHandler.ReceiveDataForbiddenDuringOpeningState = true;
                }
            }
            else
            {
                if (!_skipDataExchangeRestrictions && _sessionHandler.OpenedByReceive)
                {
                    throw new Exception("Cannot honour data exchange prohibitions since session is already opened at receive end");
                }
            }

            _pendingPromiseCallback = promiseCb;
            ContinueWindowSend(false);
            SendInProgress = true;
        }

        private void TransitionToOpenState()
        {
            _sessionHandler.State = SessionState.Opened;
            _sessionHandler.OpenedBySend = true;
            _sessionHandler.CancelOpenTimeout();
            _sessionHandler.ScheduleEnquireLinkEvent(true);
            _sessionHandler.OnOpenSuccess(false);
        }

        private bool ContinueWindowSend(bool haveSentBefore)
        {
            if (!haveSentBefore)
            {
                int minReqd = 1;
                if (_sessionHandler.State == SessionState.Opening && !_skipDataExchangeRestrictions)
                {
                    minReqd = 2;
                }
                CurrentWindowGroup = _datagramFragmenter.Next(minReqd);
                if (CurrentWindowGroup.Count < minReqd)
                {
                    throw new Exception($"Wrong fragmentation algorithm. At least {minReqd} datagram(s) must be returned, " +
                        $"but received {CurrentWindowGroup.Count}");
                }
            }
            else if (SentDatagramCountInCurrentWindowGroup >= CurrentWindowGroup.Count)
            {
                CurrentWindowGroup = _datagramFragmenter.Next(0);
                if (CurrentWindowGroup.Count == 0)
                {
                    // No more datagrams found for send window.
                    return false;
                }
                SentDatagramCountInCurrentWindowGroup = 0;
            }

            // try and fetch remainder in current window group, but respect constraint of max send window size.
            // ensure minimum of 1 for max send window size.
            int maxSendWindowSize = Math.Max(1, _sessionHandler.MaxWindowSize);
            if (_sessionHandler.State == SessionState.Opening && !haveSentBefore && !_skipDataExchangeRestrictions)
            {
                maxSendWindowSize = 1;
            }
            var nextWindow = CurrentWindowGroup.GetRange(SentDatagramCountInCurrentWindowGroup, Math.Min(maxSendWindowSize,
                CurrentWindowGroup.Count - SentDatagramCountInCurrentWindowGroup));
            SentDatagramCountInCurrentWindowGroup += nextWindow.Count;

            if (SentDatagramCountInCurrentWindowGroup == 0)
            {
                var firstMsgInNextWindow = nextWindow[0];
                if (firstMsgInNextWindow.Options == null)
                {
                    firstMsgInNextWindow.Options = new ProtocolDatagramOptions();
                }
                firstMsgInNextWindow.Options.IsFirstInWindowGroup = true;
            }
            var lastMsgInNextWindow = nextWindow[nextWindow.Count - 1];
            if (lastMsgInNextWindow.Options == null)
            {
                lastMsgInNextWindow.Options = new ProtocolDatagramOptions();
            }
            lastMsgInNextWindow.Options.IsLastInWindow = true;
            if (SentDatagramCountInCurrentWindowGroup >= CurrentWindowGroup.Count)
            {
                lastMsgInNextWindow.Options.IsLastInWindowGroup = true;
            }

            foreach (var datagram in nextWindow)
            {
                datagram.OpCode = ProtocolDatagram.OpCodeData;
                datagram.SessionId = _sessionHandler.SessionId;

                // apply to all datagrams in window if set since they
                // may arrive at different ordering at receiver, and it is
                // the first which will have an impact.
                if (_sessionHandler.State == SessionState.Opening && _skipDataExchangeRestrictions)
                {
                    if (datagram.Options == null)
                    {
                        datagram.Options = new ProtocolDatagramOptions();
                    }
                    datagram.Options.SkipDataExchangeRestrictionsDueToOpeningState = true;
                }
                // the rest will be set by assistant handlers
            }

            _sendWindowHandler = _sessionHandler.CreateSendHandlerAssistant();
            _sendWindowHandler.ProspectiveWindowToSend = nextWindow;
            _sendWindowHandler.SuccessCallback = OnWindowSendSuccess;
            _sendWindowHandler.ErrorCallback = OnWindowSendError;
            _sendWindowHandler.TimeoutCallback = () => OnWindowSendError(
                    new ProtocolOperationException(ProtocolOperationException.ErrorCodeSendTimeout));

            // Found some datagrams to send in next window.
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueWindowSend(true))
            {
                // another window was found to continue sending
                return;
            }

            // send data succeeded.

            SendInProgress = false;

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
            _datagramFragmenter = null;
            CurrentWindowGroup = null;
        }

        private void OnWindowSendError(ProtocolOperationException error)
        {
            SendInProgress = false;

            _pendingPromiseCallback.CompleteExceptionally(error);
            _pendingPromiseCallback = null;
            _datagramFragmenter = null;
            CurrentWindowGroup = null;

            // notify application layer.
            _sessionHandler.OnSendError(error);

            // revert receive prohibition
            if (_sessionHandler.State == SessionState.Opening)
            {
                _sessionHandler.ReceiveDataForbiddenDuringOpeningState = false;
            }
        }
    }
}
