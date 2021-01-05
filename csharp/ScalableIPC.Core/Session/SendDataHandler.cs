using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private ProtocolDatagramFragmenter _datagramFragmenter;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private IRetrySendHandlerAssistant _sendWindowHandler;

        public SendDataHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }
        
        internal List<ProtocolDatagram> CurrentWindowGroup { get; private set; }
        internal int SentDatagramCountInCurrentWindowGroup { get; private set; }
        public bool SendInProgress { get; set; }

        public void PrepareForDispose(SessionDisposedException cause)
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

        public void Dispose(SessionDisposedException cause)
        {
            // nothing to do
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeDataAck)
            {
                return false;
            }

            // to prevent clashes with close handler, check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sendWindowHandler.OnAckReceived(datagram);
            return true;
        }

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            ProcessSendRequest(message, promiseCb);
            return true;
        }

        private void ProcessSendRequest(ProtocolMessage message,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            _pendingPromiseCallback = promiseCb;

            // ensure minimum of 512 and maximum = datagram max length
            int mtu = Math.Min(Math.Max(ProtocolDatagram.MinimumTransferUnitSize, 
                _sessionHandler.MaximumTransferUnitSize), ProtocolDatagram.MaxDatagramSize);
            _datagramFragmenter = new ProtocolDatagramFragmenter(message, mtu, null);

            // reset fields used for continuation.
            SentDatagramCountInCurrentWindowGroup = 0;

            ContinueWindowSend(false);
            SendInProgress = true;
        }

        private bool ContinueWindowSend(bool haveSentBefore)
        {
            if (!haveSentBefore)
            {
                CurrentWindowGroup = _datagramFragmenter.Next();
                if (CurrentWindowGroup.Count == 0)
                {
                    throw new Exception("Wrong fragmentation algorithm. At least one datagram must be returned");
                }
            }
            else if (SentDatagramCountInCurrentWindowGroup >= CurrentWindowGroup.Count)
            {
                CurrentWindowGroup = _datagramFragmenter.Next();
                if (CurrentWindowGroup.Count == 0)
                {
                    // No more datagrams found for send window.
                    return false;
                }
                SentDatagramCountInCurrentWindowGroup = 0;
            }

            // try and fetch remainder in current window group, but respect constraint of max send window size.
            // ensure minimum of 1 for max send window size.
            int maxSendWindowSize = Math.Max(1, _sessionHandler.MaxSendWindowSize);
            var nextWindow = CurrentWindowGroup.GetRange(SentDatagramCountInCurrentWindowGroup, Math.Min(maxSendWindowSize,
                CurrentWindowGroup.Count - SentDatagramCountInCurrentWindowGroup));
            SentDatagramCountInCurrentWindowGroup += nextWindow.Count;

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
                // the rest wil be set by assistant handlers
            }

            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = nextWindow,
                SuccessCallback = OnWindowSendSuccess,
                DisposeCallback = OnWindowSendError
            };

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

        private void OnWindowSendError(SessionDisposedException error)
        {
            _sessionHandler.InitiateDispose(error);
        }
    }
}
