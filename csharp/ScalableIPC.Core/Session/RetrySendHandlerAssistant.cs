using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class RetrySendHandlerAssistant
    {
        private readonly IReferenceSessionHandler _sessionHandler;
        private SendHandlerAssistant _currentWindowHandler;
        private int _retryCount;

        public RetrySendHandlerAssistant(IReferenceSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public Action SuccessCallback { get; set; }
        public Action<SessionDisposedException> DisposeCallback { get; set; }

        public void OnWindowSendTimeout()
        {
            if (_retryCount >= _sessionHandler.MaxRetryCount)
            {
                // maximum retry count reached. begin disposing
                DisposeCallback.Invoke(new SessionDisposedException(false, ProtocolDatagram.AbortCodeTimeout));
            }
            else
            {
                _retryCount++;
                RetrySend(_sessionHandler.AckTimeoutSecs);
            }
        }

        public void OnAckReceived(ProtocolDatagram datagram)
        {
            _currentWindowHandler.OnAckReceived(datagram);
        }

        public void Cancel()
        {
            _currentWindowHandler?.Cancel();
        }

        public void Start()
        {
            RetrySend(_sessionHandler.AckTimeoutSecs);
        }

        public void RetrySend(int ackTimeoutSecs)
        {
            int previousSendCount = 0;
            bool stopAndWait = false;
            if (_currentWindowHandler != null)
            {
                previousSendCount = _currentWindowHandler.PreviousSendCount;
                // subsequent attempts after timeout within same window id
                // should always use stop and wait flow control.
                stopAndWait = true;
            }
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = CurrentWindow,
                TimeoutCallback = OnWindowSendTimeout,
                SuccessCallback = SuccessCallback,
                DisposeCallback = DisposeCallback,
                AckTimeoutSecs = ackTimeoutSecs,
                PreviousSendCount = previousSendCount,
                StopAndWait = stopAndWait
            };
            _currentWindowHandler.Start();
        }
    }
}
