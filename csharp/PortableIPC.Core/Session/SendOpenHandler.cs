using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private SendHandlerAssistant _currentWindowHandler;
        private int _retryCount;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private bool _inUseByBulkSend;

        public SendOpenHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress { get; set; }

        public ProtocolDatagram MessageToSend { get; set; }

        public void Shutdown(Exception error)
        {
            /*_currentWindowHandler?.Cancel();
            if (SendInProgress)
            {
                _pendingPromiseCallback.CompleteExceptionally(error);
                SendInProgress = false;
            }*/
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeOpenAck)
            {
                return false;
            }

            //ProcessAckReceipt(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }
            //ProcessSendRequest(message, promiseCb);
            return true;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        /*private void ProcessSendRequest(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != SessionState.NotStarted &&
                _sessionHandler.SessionState != SessionState.Opening)
            {
                promiseCb.CompleteExceptionally(new Exception("Invalid session state for send open"));
                return;
            }

            if (SendInProgress)
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return;
            }

            MessageToSend = message;
            SendInProgress = true;
            ProcessSendWindow(promiseCb, false);
        }

        protected internal void ProcessSendWindow(PromiseCompletionSource<VoidType> promiseCb, bool inUseByBulkSend)
        {
            _retryCount = 0;
            _currentWindowHandler = null;
            _inUseByBulkSend = inUseByBulkSend;
            _pendingPromiseCallback = promiseCb;

            RetrySend();
        }

        private void RetrySend()
        {
            // don't bother if window handler is already about to start sending requested PDUs
            if (_currentWindowHandler != null && 0 >= _currentWindowHandler.PendingPduIndex &&
                0 < _currentWindowHandler.EndIndex)
            {
                return;
            }
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = new List<ProtocolDatagram> { MessageToSend },
                FailureCallback = OnWindowSendError,
                SuccessCallback = OnWindowSendSuccess,
                PendingPduIndex = 0,
                EndIndex = 1
            };
            _currentWindowHandler.Start();
        }

        private void ProcessAckReceipt(ProtocolDatagram message)
        {
            if (!SendInProgress)
            {
                return;
            }

            if (message.SequenceNumber == _sessionHandler.NextSendSeqStart)
            {
                // indirectly cancel ack timeout.
                _sessionHandler.ResetIdleTimeout();
                _currentWindowHandler.Cancel();

                try
                {
                    // validate options.

                    if (_inUseByBulkSend)
                    {
                        SendInProgress = false;
                    }

                    if (message.IsLastInOpenRequest == true)
                    {
                        _sessionHandler.SessionState = SessionState.OpenedForData;
                    }

                    _sessionHandler.NextSendSeqStart = ProtocolDatagram.ComputeNextSequenceStart(_sessionHandler.NextSendSeqStart, 1);

                    _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
                }
                catch (Exception error)
                {
                    _sessionHandler.ProcessShutdown(error, false);
                }
            }
        }

        private void ProcessAckTimeout()
        {
            if (_retryCount >= _sessionHandler.EndpointHandler.EndpointConfig.MaxRetryCount)
            {
                _sessionHandler.ProcessShutdown(null, true);
            }
            else
            {
                _retryCount++;
                RetrySend();
            }
        }

        internal void OnWindowSendError(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        internal void OnWindowSendSuccess()
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ResetAckTimeout(_sessionHandler.EndpointHandler.EndpointConfig.AckTimeoutSecs, 
                    () => ProcessAckTimeout());
            });
        }*/
    }
}
