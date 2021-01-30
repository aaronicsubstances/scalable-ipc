using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface IFireAndForgetSendHandlerAssistant
    {
        ProtocolDatagram MessageToSend { get; set; }
        bool Sent { get; set; }
        Action SuccessCallback { get; set; }
        Action<ProtocolOperationException> ErrorCallback { get; set; }
        bool IsComplete { get; set; }
        void Start();
        void Cancel();
    }
}
