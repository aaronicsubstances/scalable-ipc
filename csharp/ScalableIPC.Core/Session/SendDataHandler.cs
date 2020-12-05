using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private ProtocolDatagramFragmenter _datagramFragmenter;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private RetrySendHandlerAssistant _sendWindowHandler;
        private List<ProtocolDatagram> _currentWindowGroup;
        private int _sentDatagramCountInCurrentWindowGroup;

        public SendDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _currentWindowGroup = new List<ProtocolDatagram>();
            _sentDatagramCountInCurrentWindowGroup = 0;
        }

        public bool SendInProgress { get; set; }

        public void PrepareForDispose(SessionDisposedException cause)
        {
            _sendWindowHandler?.Cancel();
        }

        public void Dispose(SessionDisposedException cause)
        {
            _sendWindowHandler?.Cancel();
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                _sessionHandler.Log("9316f65d-f2bd-4877-b929-a9f02b545d3c", "Send data failed");

                _pendingPromiseCallback.CompleteExceptionally(cause);
                _pendingPromiseCallback = null;
            }
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeAck)
            {
                return false;
            }

            // to prevent clashes with close handler, check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sessionHandler.Log("4b1d1ab5-f38a-478d-b444-b43cdf9f363a", datagram,
                "Ack datagram accepted for processing in send data handler");
            _sendWindowHandler.OnAckReceived(datagram);
            return true;
        }

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            _sessionHandler.Log("75cb01ea-3901-40da-b104-dfc3914e2edd",
                "Message accepted for processing in send data handler");
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
            _sentDatagramCountInCurrentWindowGroup = 0;

            ContinueWindowSend(false);
            SendInProgress = true;
        }

        private bool ContinueWindowSend(bool haveSentBefore)
        {
            _sessionHandler.Log("c5b21878-ac61-4414-ba37-4248a4702084",
                (haveSentBefore ? "Attempting to continue ": "About to start") + " sending data");

            if (!haveSentBefore)
            {
                _currentWindowGroup = _datagramFragmenter.Next();
                if (_currentWindowGroup.Count == 0)
                {
                    throw new Exception("Wrong fragmentation algorithm. At least one datagram must be returned");
                }
            }
            else if (_sentDatagramCountInCurrentWindowGroup >= _currentWindowGroup.Count)
            {
                _currentWindowGroup = _datagramFragmenter.Next();
                if (_currentWindowGroup.Count == 0)
                {
                    _sessionHandler.Log("d7d65563-154a-4855-8efd-c19ae60817d8",
                        "No more datagrams found for send window.");
                    return false;
                }
                _sentDatagramCountInCurrentWindowGroup = 0;
            }

            // try and fetch remainder in current window group, but respect constraint of max send window size.
            // ensure minimum of 1 for max send window size.
            int maxSendWindowSize = Math.Max(1, _sessionHandler.MaxSendWindowSize);
            var nextWindow = _currentWindowGroup.GetRange(_sentDatagramCountInCurrentWindowGroup, Math.Min(maxSendWindowSize,
                _currentWindowGroup.Count - _sentDatagramCountInCurrentWindowGroup));
            _sentDatagramCountInCurrentWindowGroup += nextWindow.Count;

            var lastMsgInNextWindow = nextWindow[nextWindow.Count - 1];
            if (lastMsgInNextWindow.Options == null)
            {
                lastMsgInNextWindow.Options = new ProtocolDatagramOptions();
            }
            lastMsgInNextWindow.Options.IsLastInWindow = true;
            if (_sentDatagramCountInCurrentWindowGroup >= _currentWindowGroup.Count)
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
                SuccessCallback = OnWindowSendSuccess
            };

            _sessionHandler.Log("d151c5bf-e922-4828-8820-8cf964dac160",
                $"Found {nextWindow.Count} datagrams to send in next window.", 
                "count", nextWindow.Count);
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueWindowSend(true))
            {
                _sessionHandler.Log("d2dd3b31-8630-481d-9f18-4b91dd8345c3", 
                    "Found another window to continue sending");
                return;
            }

            SendInProgress = false;

            _sessionHandler.Log("edeafec4-5596-4931-9f2a-5876e1241d89", "Send data succeeded",
                "sendInProgress", _sessionHandler.IsSendInProgress());

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
            _datagramFragmenter = null;
            _currentWindowGroup = null;
        }
    }
}
