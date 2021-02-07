using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendHandlerAssistant: ISendHandlerAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private ISendWindowAssistant _currentWindowHandler;

        public SendHandlerAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> ProspectiveWindowToSend { get; set; }
        public Action SuccessCallback { get; set; }
        public Action TimeoutCallback { get; set; }
        public Action<ProtocolOperationException> ErrorCallback { get; set; }
        public List<int> ActualSentWindowRanges { get; private set; }
        public List<ProtocolDatagram> CurrentWindow { get; private set; }
        public int RetryCount { get; private set; }
        public int TotalSentCount { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsComplete { get; private set; } = false;

        public void Start()
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }
            if (IsStarted)
            {
                return;
            }

            ActualSentWindowRanges = new List<int>();
            IsStarted = true;
            ContinueSend();
        }

        public void OnAckReceived(ProtocolDatagram datagram)
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }
            if (!IsStarted)
            {
                throw new Exception("handler has not been started");
            }

            _currentWindowHandler?.OnAckReceived(datagram);
        }

        public void Cancel()
        {
            IsComplete = true;
            _currentWindowHandler?.Cancel();
        }

        private void ContinueSend()
        {
            _currentWindowHandler?.Cancel();
            int effectivePendingWindowCount = ProspectiveWindowToSend.Count - TotalSentCount;
            // use current remote window size to limit pending window count.
            if (_sessionHandler.RemoteMaxWindowSize.HasValue &&
                _sessionHandler.RemoteMaxWindowSize.Value > 0)
            {
                effectivePendingWindowCount = Math.Min(effectivePendingWindowCount,
                    _sessionHandler.RemoteMaxWindowSize.Value);
            }
            CurrentWindow = ProspectiveWindowToSend.GetRange(TotalSentCount, effectivePendingWindowCount);

            // check if we are done sending (or empty to start with).
            if (CurrentWindow.Count == 0)
            {
                IsComplete = true;
                SuccessCallback.Invoke();
                return;
            }

            RetryCount = 0;
            _currentWindowHandler = _sessionHandler.CreateSendWindowAssistant();
            _currentWindowHandler.CurrentWindow = CurrentWindow;
            _currentWindowHandler.WindowFullCallback = OnWindowFull;
            _currentWindowHandler.TimeoutCallback = OnWindowSendTimeout;
            _currentWindowHandler.ErrorCallback = OnWindowSendError;
            _currentWindowHandler.Start();
        }

        private void OnWindowFull(int sentCount)
        {
            _sessionHandler.OpenedStateConfirmedForSend = true;
            if (_sessionHandler.State == SessionState.Opening)
            {
                _sessionHandler.State = SessionState.Opened;
                _sessionHandler.CancelOpenTimeout();
                _sessionHandler.ScheduleEnquireLinkEvent(true);
            }
            ActualSentWindowRanges.Add(sentCount);
            TotalSentCount += sentCount;
            ContinueSend();
        }

        private void OnWindowSendError(ProtocolOperationException error)
        {
            IsComplete = true;
            ErrorCallback.Invoke(error);
        }

        private void OnWindowSendTimeout()
        {
            if (RetryCount >= _sessionHandler.MaxRetryCount)
            {
                // maximum retry count reached.
                // end retries and signal timeout to application layer.
                IsComplete = true;
                TimeoutCallback.Invoke();
                return;
            }

            RetryCount++;
            _currentWindowHandler = _sessionHandler.CreateSendWindowAssistant();
            _currentWindowHandler.SendOneAtATime = true;
            _currentWindowHandler.CurrentWindow = CurrentWindow;
            _currentWindowHandler.WindowFullCallback = OnWindowFull;
            _currentWindowHandler.TimeoutCallback = OnWindowSendTimeout;
            _currentWindowHandler.ErrorCallback = OnWindowSendError;
            _currentWindowHandler.RetryCount = RetryCount;
            _currentWindowHandler.Start();
        }
    }
}
