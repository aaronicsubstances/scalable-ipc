using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class RetrySendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private SendHandlerAssistant _currentWindowHandler;

        public RetrySendHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public Action SuccessCallback { get; set; }
        protected internal int RetryCount { get; set; }

        public virtual void OnWindowSendTimeout()
        {
            if (RetryCount >= _sessionHandler.MaxRetryCount)
            {
                _sessionHandler.Log("28a90da5-1113-42e5-9894-881e3e2876f5", 
                    "Maximum retry count reached. Shutting down...", "retryCount", RetryCount);
                _sessionHandler.ProcessShutdown(null, true);
            }
            else
            {
                RetryCount++;
                RetrySend(_sessionHandler.AckTimeoutSecs);
            }
        }

        public void OnAckReceived(ProtocolDatagram message)
        {
            _currentWindowHandler.OnAckReceived(message);
        }

        public void Cancel()
        {
            _currentWindowHandler?.Cancel();
        }

        public virtual void Start()
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
                AckTimeoutSecs = ackTimeoutSecs,
                PreviousSendCount = previousSendCount,
                StopAndWait = stopAndWait
            };
            _sessionHandler.Log("ea2d46d1-8baf-4e31-9c1a-5740ad5529cd", 
                RetryCount > 0 ? "Retry sending window" : "Start sending window",
                "retryCount", RetryCount, "prevSendCount", _currentWindowHandler.PreviousSendCount,
                "lastOpenRequest", CurrentWindow[CurrentWindow.Count - 1].IsLastOpenRequest,
                "lastInWindow", CurrentWindow[CurrentWindow.Count - 1].IsLastInWindow);
            _currentWindowHandler.Start();
        }
    }
}
