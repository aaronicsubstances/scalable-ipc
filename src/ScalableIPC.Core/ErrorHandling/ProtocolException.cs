using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ErrorHandling
{
    public class ProtocolException: Exception
    {
        private static string GenerateMessage(ProtocolErrorCode errorCode)
        {
            bool causedByRemotePeer = errorCode.Value < 0;
            string suffix = "Caused by " + (causedByRemotePeer ? "remote" : "local") + " peer";
            return errorCode.Description + " " + suffix;
        }

        public ProtocolException(ProtocolErrorCode errorCode) :
            this(errorCode, null, null)
        { }

        public ProtocolException(ProtocolErrorCode errorCode, Exception innerException) :
            this(errorCode, null, innerException)
        { }

        public ProtocolException(ProtocolErrorCode errorCode, string message,
                Exception innerException) :
            base(message ?? GenerateMessage(errorCode ?? ProtocolErrorCode.ApplicationError), innerException)
        {
            ErrorCode = errorCode ?? ProtocolErrorCode.ApplicationError;
        }

        public ProtocolErrorCode ErrorCode { get; }
    }
}
