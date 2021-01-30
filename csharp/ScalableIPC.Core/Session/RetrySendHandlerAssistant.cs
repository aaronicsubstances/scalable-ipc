using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class RetrySendHandlerAssistant: IRetrySendHandlerAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private ISendHandlerAssistant _currentWindowHandler;

        public RetrySendHandlerAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public Action SuccessCallback { get; set; }
        public Action<SessionDisposedException> DisposeCallback { get; set; }
        public int RetryCount { get; set; }
        public DateTime RetryStartTime { get; set; }
        public int TotalSentCount { get; set; }

        public void Start()
        {
            RetryStartTime = DateTime.UtcNow;
            RetrySend(false);
        }

        public void OnAckReceived(ProtocolDatagram datagram)
        {
            _currentWindowHandler?.OnAckReceived(datagram);
        }

        public void Cancel()
        {
            _currentWindowHandler?.Cancel();
        }

        private void OnWindowSendTimeout()
        {
            if (RetryCount >= _sessionHandler.MaxRetryCount)
            {
                // maximum retry count reached. begin disposing
                DisposeCallback.Invoke(new SessionDisposedException(false, ProtocolDatagram.AbortCodeTimeout));
            }
            else
            {
                if (_sessionHandler.MaxRetryPeriod > 0)
                {
                    var retryPeriod = (DateTime.UtcNow - RetryStartTime).TotalMilliseconds;
                    if (retryPeriod >= _sessionHandler.MaxRetryPeriod)
                    {
                        // maximum retry time period reached. begin disposing
                        DisposeCallback.Invoke(new SessionDisposedException(false, ProtocolDatagram.AbortCodeTimeout));
                        return;
                    }
                }

                RetryCount++;
                // subsequent attempts after timeout within same window id
                // should always use stop and wait flow control.
                RetrySend(true);
            }
        }

        private void RetrySend(bool stopAndWait)
        {
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = _sessionHandler.CreateSendHandlerAssistant();
            var pendingWindow = CurrentWindow.GetRange(TotalSentCount, CurrentWindow.Count - TotalSentCount);
            _currentWindowHandler.CurrentWindow = pendingWindow;
            _currentWindowHandler.WindowFullCallback = OnWindowFull;
            _currentWindowHandler.TimeoutCallback = OnWindowSendTimeout;
            _currentWindowHandler.SuccessCallback = SuccessCallback;
            _currentWindowHandler.DisposeCallback = DisposeCallback;
            _currentWindowHandler.StopAndWait = stopAndWait;
            _currentWindowHandler.Start();
        }

        private void OnWindowFull(int sentCount)
        {
            TotalSentCount += sentCount;
            // reset retry count for new window.
            RetryCount = 0;
            RetryStartTime = DateTime.UtcNow;
            RetrySend(false);
        }
    }
}
