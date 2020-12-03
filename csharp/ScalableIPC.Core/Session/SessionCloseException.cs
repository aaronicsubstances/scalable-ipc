using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SessionCloseException: Exception
    {
        public static readonly int ReasonNone = 0;
        public static readonly int ReasonTimeout = 1;
        public static readonly int ReasonCloseReceived = 2;
        public static readonly int ReasonCloseAllReceived = 3;
        public static readonly int ReasonShutdown = 4;
        public static readonly int ReasonError = 5;
        public static readonly int ReasonGracefulUserRequest = 6;
        public static readonly int ReasonForcefulUserRequest = 6;

        private static string StringifyReason(int reason)
        {
            if (reason == ReasonNone)
                return "UNSPECIFIED";
            if (reason == ReasonTimeout)
                return "TIMEOUT";
            if (reason == ReasonCloseReceived)
                return "CLOSERECVD";
            if (reason == ReasonCloseAllReceived)
                return "CLOSEALLRECVD";
            if (reason == ReasonShutdown)
                return "SHUTTINGDOWN";
            if (reason == ReasonError)
                return "INTERNALERROR";
            if (reason == ReasonGracefulUserRequest)
                return "NORMAL";
            if (reason == ReasonForcefulUserRequest)
                return "FORCEDCLOSE";
            return null;
        }

        public SessionCloseException(int reason):
            this(reason, null)
        { }

        public SessionCloseException(int reason, int? closeErrorCode):
            base(StringifyReason(reason))
        {
            Reason = reason;
            ErrorCode = closeErrorCode;
        }

        public SessionCloseException(Exception innerException):
            this(ReasonError, null, StringifyReason(ReasonError), innerException)
        { }

        public SessionCloseException(int reason, int? closeErrorCode, string message, Exception innerException):
            base(message, innerException)
        {
            Reason = reason;
            ErrorCode = closeErrorCode;
        }

        public SessionCloseException(SerializationInfo serializationInfo, StreamingContext streamingContext) :
            base(serializationInfo, streamingContext)
        { }

        public int Reason { get; }
        public int? ErrorCode { get; }
    }
}
