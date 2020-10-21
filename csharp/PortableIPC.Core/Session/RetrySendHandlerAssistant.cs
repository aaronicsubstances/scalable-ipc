using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
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
            int retryIndex = 0;
            bool stopAndWait = false;
            if (_currentWindowHandler != null)
            {
                retryIndex = _currentWindowHandler.StartIndex;

                // NB: alternate between stop and wait and go back N, in between timeouts. 
                stopAndWait = !_currentWindowHandler.StopAndWait;
            }
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = CurrentWindow,
                TimeoutCallback = OnWindowSendTimeout,
                SuccessCallback = SuccessCallback,
                AckTimeoutSecs = ackTimeoutSecs,
                StartIndex = retryIndex,
                StopAndWait = stopAndWait
            };
            _currentWindowHandler.Start();
        }
    }
}
