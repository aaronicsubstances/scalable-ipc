using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly SendHandler _sendHandler;

        public SendHandlerAssistant(ISessionHandler sessionHandler, SendHandler sendHandler, int startIndex)
        {
            _sessionHandler = sessionHandler;
            _sendHandler = sendHandler;

            PendingPduIndex = startIndex;
        }

        public int PendingPduIndex { get; set; }

        public void Cancel()
        {
            if (!IsCancelled)
            {
                IsCancelled = true;
            }
        }

        public bool IsCancelled { get; set; } = false;

        public void Start()
        {
            ContinueSending();
        }

        private void ContinueSending()
        {
            if (IsCancelled)
            {
                return;
            }
            if (PendingPduIndex == _sendHandler.CurrentWindow.Count)
            {
                _sendHandler.OnWindowSendSuccess();
                return;
            }
            var nextMessage = _sendHandler.CurrentWindow[PendingPduIndex++];
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, nextMessage)
                .Then(HandleSendSuccess, HandleSendError);
        }

        private VoidType HandleSendSuccess(VoidType _)
        {
            _sessionHandler.PostSeriallyIfNotClosed(ContinueSending);
            return VoidType.Instance;
        }

        private void HandleSendError(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                if (!IsCancelled)
                {
                    _sendHandler.OnWindowSendError(error);
                }
            });
        }
    }
}
