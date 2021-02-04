using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolOperationException: Exception
    {
        public const int ErrorCodeNormalClose = 1;
        public const int ErrorCodeForceClose = 2;
        public const int ErrorCodeOpenTimeout = 3;
        public const int ErrorCodeIdleTimeout = 4;
        public const int ErrorCodeInternalApplicationError = 5;
        public const int ErrorCodeWindowGroupOverflow = 6;
        public const int ErrorCodeOptionDecodingError = 7;

        // The following error codes are not meant to be used for network
        // communications. As such they are negative.
        public const int ErrorCodeRestart = -1;
        public const int ErrorCodeShutdown = -2;
        public const int ErrorCodeSendTimeout = -3;

        public static string StringifyReason(bool causedByRemotePeer, int errorCode)
        {
            string reasonPhrase = FormatErrorCode(errorCode);
            string suffix = "Caused by " + (causedByRemotePeer ? "remote" : "local") + " peer";
            return reasonPhrase + " " + suffix;
        }

        public static string FormatErrorCode(int code)
        {
            if (code == 0)
                return "NONE";
            if (code == ErrorCodeNormalClose)
                return "NORMAL CLOSE";
            else if (code == ErrorCodeIdleTimeout)
                return "IDLE_TIMEOUT";
            else if (code == ErrorCodeSendTimeout)
                return "SEND_TIMEOUT";
            else if (code == ErrorCodeForceClose)
                return "FORCED CLOSE";
            else if (code == ErrorCodeInternalApplicationError)
                return "INTERNAL ERROR";
            else if (code == ErrorCodeWindowGroupOverflow)
                return "WINDOW GROUP OVERLFOW";
            else if (code == ErrorCodeRestart)
                return "RESTART";
            else if (code == ErrorCodeShutdown)
                return "SHUTTING DOWN";
            else if (code == ErrorCodeOptionDecodingError)
                return "DATAGRAM OPTION DECODING ERROR";
            else
                return $"UNKNOWN ({code})";
        }

        public ProtocolOperationException(bool causedByRemotePeer, int errorCode):
            base(StringifyReason(causedByRemotePeer, errorCode))
        {
            CausedByRemotePeer = causedByRemotePeer;
            ErrorCode = errorCode;
        }

        public ProtocolOperationException(Exception innerException):
            this(false, ErrorCodeInternalApplicationError, StringifyReason(false, ErrorCodeInternalApplicationError),
                innerException)
        { }

        public ProtocolOperationException(bool causedByRemotePeer, int errorCode, Exception innerException) :
            base(StringifyReason(false, ErrorCodeInternalApplicationError), innerException)
        {
            CausedByRemotePeer = causedByRemotePeer;
            ErrorCode = errorCode;
        }

        public ProtocolOperationException(bool causedByRemotePeer, int errorCode, string message,
                Exception innerException):
            base(message, innerException)
        {
            CausedByRemotePeer = causedByRemotePeer;
            ErrorCode = errorCode;
        }

        public bool CausedByRemotePeer { get; }

        public int ErrorCode { get; }
    }
}
