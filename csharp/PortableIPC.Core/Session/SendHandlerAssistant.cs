using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace PortableIPC.Core.Session
{
    public class SendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;

        public SendHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }

        public int PendingPduIndex { get; set; }
        public int EndIndex { get; set; }
        public Action SuccessCallback { get; set; }
        public Action<Exception> FailureCallback { get; set; }

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
            if (PendingPduIndex >= EndIndex)
            {
                SuccessCallback.Invoke();
                return;
            }
            var nextMessage = CurrentWindow[PendingPduIndex++];
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, nextMessage)
                .Then(HandleSendSuccess, HandleSendError);
        }

        private VoidType HandleSendSuccess(VoidType _)
        {
            _sessionHandler.PostSerially(ContinueSending);
            return VoidType.Instance;
        }

        private void HandleSendError(Exception error)
        {
            _sessionHandler.PostSerially(() =>
            {
                if (!IsCancelled)
                {
                    FailureCallback.Invoke(error);
                }
            });
        }
    }
}
