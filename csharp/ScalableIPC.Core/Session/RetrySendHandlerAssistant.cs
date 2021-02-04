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
        public Action TimeoutCallback { get; set; }
        public Action<ProtocolOperationException> ErrorCallback { get; set; }
        public int RetryCount { get; private set; }
        public int TotalSentCount { get; private set; }
        public bool IsComplete { get; private set; } = false;

        public void Start()
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }

            RetryCount = 0;
            TotalSentCount = 0;

            RetrySend(false);
        }

        public void OnAckReceived(ProtocolDatagram datagram)
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }

            _currentWindowHandler?.OnAckReceived(datagram);
        }

        public void Cancel()
        {
            IsComplete = true;
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

            if (tryAgain)
            {
                RetryCount++;
                // subsequent attempts after timeout within same window id
                // should always use stop and wait flow control.
                RetrySend(true);
            }
            else
            {
                // end retries and signal timeout to application layer.
                IsComplete = true;
                TimeoutCallback.Invoke();
            }
        }

        private void RetrySend(bool stopAndWait)
        {
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = _sessionHandler.CreateSendHandlerAssistant();
            int effectivePendingWindowCount = CurrentWindow.Count - TotalSentCount;
            // use current remote window size to limit pending window count.
            if (_sessionHandler.RemoteMaxWindowSize.HasValue &&
                _sessionHandler.RemoteMaxWindowSize.Value > 0)
            {
                effectivePendingWindowCount = Math.Min(effectivePendingWindowCount,
                    _sessionHandler.RemoteMaxWindowSize.Value);
            }
            var pendingWindow = CurrentWindow.GetRange(TotalSentCount, effectivePendingWindowCount);
            _currentWindowHandler.CurrentWindow = pendingWindow;
            _currentWindowHandler.WindowFullCallback = OnWindowFull;
            _currentWindowHandler.TimeoutCallback = OnWindowSendTimeout;
            _currentWindowHandler.SuccessCallback = OnWindowSendSuccess;
            _currentWindowHandler.ErrorCallback = OnWindowSendError;
            _currentWindowHandler.StopAndWait = stopAndWait;
            _currentWindowHandler.RetryCount = RetryCount;
            _currentWindowHandler.Start();
        }

        private void OnWindowSendSuccess()
        {
            IsComplete = true;
            SuccessCallback.Invoke();
        }

        private void OnWindowSendError(ProtocolOperationException error)
        {
            IsComplete = true;
            ErrorCallback.Invoke(error);
        }

        private void OnWindowFull(int sentCount)
        {
            TotalSentCount += sentCount;
            // reset retry count for new window.
            RetryCount = 0;
            RetrySend(false);
        }
    }
}
