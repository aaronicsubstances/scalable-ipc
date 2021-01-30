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
        public Action<ProtocolOperationException> ErrorCallback { get; set; }
        public Action<ProtocolOperationException> DisposeCallback { get; set; }
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
            bool tryAgain = true;
            if (RetryCount >= _sessionHandler.MaxRetryCount)
            {
                // maximum retry count reached.
                tryAgain = false;
            }            
            if (_sessionHandler.MaxRetryPeriod > 0)
            {
                var retryPeriod = (DateTime.UtcNow - RetryStartTime).TotalMilliseconds;
                if (retryPeriod >= _sessionHandler.MaxRetryPeriod)
                {
                    // maximum retry time period reached
                    tryAgain = false;
                }
            }

            if (tryAgain)
            {
                RetryCount++;
                // subsequent attempts after timeout within same window id
                // should always use stop and wait flow control.
                RetrySend(true);
            }
            else
            {
                // begin disposing
                DisposeCallback.Invoke(new ProtocolOperationException(false, ProtocolOperationException.ErrorCodeTimeout));
            }
        }

        private void RetrySend(bool stopAndWait)
        {
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = _sessionHandler.CreateSendHandlerAssistant();
            int effectivePendingWindowCount = CurrentWindow.Count - TotalSentCount;
            // use current remote window size to limit pending window count.
            if (_sessionHandler.MaxRemoteWindowSize > 0)
            {
                effectivePendingWindowCount = Math.Min(effectivePendingWindowCount,
                    _sessionHandler.MaxRemoteWindowSize);
            }
            var pendingWindow = CurrentWindow.GetRange(TotalSentCount, effectivePendingWindowCount);
            _currentWindowHandler.CurrentWindow = pendingWindow;
            _currentWindowHandler.WindowFullCallback = OnWindowFull;
            _currentWindowHandler.TimeoutCallback = OnWindowSendTimeout;
            _currentWindowHandler.SuccessCallback = SuccessCallback;
            _currentWindowHandler.ErrorCallback = ErrorCallback;
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
