using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private SendHandlerAssistant _currentWindowHandler;
        private int _retryCount;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private bool _inUseByBulkSend;

        public SendDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        protected internal List<ProtocolDatagram> CurrentWindow { get; } = new List<ProtocolDatagram>();
        protected internal bool SendInProgress { get; set; }

        public void Shutdown(Exception error)
        {
            _currentWindowHandler?.Cancel();
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                var cb = _pendingPromiseCallback;
                _pendingPromiseCallback = null;
                _sessionHandler.PostNonSerially(() => cb.CompleteExceptionally(error));
            }
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeAck)
            {
                return false;
            }

            ProcessAckReceipt(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            ProcessSendRequest(message, promiseCb);
            return true;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessSendRequest(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState == SessionState.OpenedForData)
            {
                _sessionHandler.PostNonSerially(() =>
                    promiseCb.CompleteExceptionally(new Exception("Invalid session state for send data")));
                return;
            }

            if (SendInProgress)
            {
                _sessionHandler.PostNonSerially(() =>
                    promiseCb.CompleteExceptionally(new Exception("Send in progress")));
                return;
            }

            // reset current window.
            CurrentWindow.Clear();
            message.IsLastInWindow = true;
            CurrentWindow.Add(message);

            SendInProgress = true;
            ProcessSendWindow(promiseCb, false);
        }

        protected internal void ProcessSendWindow(PromiseCompletionSource<VoidType> promiseCb, bool inUseByBulkSend)
        {
            _retryCount = 0;
            _currentWindowHandler = null;
            _pendingPromiseCallback = promiseCb;
            _inUseByBulkSend = inUseByBulkSend;

            RetrySend(CalculateAckTimeoutSecs(0));
        }

        public virtual int CalculateAckTimeoutSecs(int retryCount)
        {
            return _sessionHandler.AckTimeoutSecs;
        }

        private void RetrySend(int ackTimeoutSecs)
        {
            int retryIndex = 0;
            bool stopAndWait = false;
            if (_currentWindowHandler != null)
            {
                retryIndex = _currentWindowHandler.StartIndex;

                // NB: alternate between stop and wait and go back n in between timeouts. 
                stopAndWait = !_currentWindowHandler.StopAndWait;
            }
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = CurrentWindow,
                TimeoutCallback = OnWindowSendTimeout,
                SuccessCallback = OnWindowSendSuccess,
                AckTimeoutSecs = ackTimeoutSecs,
                StartIndex = retryIndex,
                StopAndWait = stopAndWait
            };
            _currentWindowHandler.Start();
        }

        private void ProcessAckReceipt(ProtocolDatagram ack)
        {
            if (!SendInProgress)
            {
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            _currentWindowHandler.OnAckReceived(ack);            
        }

        private void OnWindowSendTimeout()
        {
            if (_retryCount >= _sessionHandler.MaxRetryCount)
            {
                _sessionHandler.ProcessShutdown(null, true);
            }
            else
            {
                _retryCount++;
                RetrySend(CalculateAckTimeoutSecs(_retryCount));
            }
        }

        private void OnWindowSendSuccess()
        {
            // move window bounds
            _sessionHandler.IncrementNextWindowIdToSend();

            // if bulk sending, let bulk send handler be the one to determine when to stop
            // sending.
            if (!_inUseByBulkSend)
            {
                SendInProgress = false;
            }

            var cb = _pendingPromiseCallback;
            _pendingPromiseCallback = null;

            // complete pending promise.
            _sessionHandler.PostNonSerially(() =>
            {
                cb.CompleteSuccessfully(VoidType.Instance);
            });
        }
    }
}
