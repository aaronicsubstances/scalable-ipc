using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolOperationException: Exception
    {
        // close codes.
        public const int ErrorCodeNormalClose = 1;
        public const int ErrorCodeForceClose = 2;
        public const int ErrorCodeIdleTimeout = 3;
        public const int ErrorCodeInternalError = 4;

        // enquire link codes
        public const int ErrorCodeSessionOk = 150;
        public const int ErrorCodeSessionEnded = 151;

        // data codes.
        public const int ErrorCodeWindowGroupOverflow = 250;
        public const int ErrorCodeOptionDecodingError = 251;

        // The following error codes are not meant to be used for network
        // communications. As such they are negative.
        public const int ErrorCodeRestart = -1;
        public const int ErrorCodeShutdown = -2;
        public const int ErrorCodeSendTimeout = -3;
        public const int ErrorCodeOpenTimeout = -4;
        public const int ErrorCodeApplicationError = -5;

        public static int FetchExpectedErrorCode(ProtocolDatagram datagram)
        {
            int? errorCode = datagram.Options?.ErrorCode;
            switch (datagram.OpCode)
            {
                case ProtocolDatagram.OpCodeClose:
                    if (errorCode == null || errorCode < 1 || errorCode >= 100)
                    {
                        return ErrorCodeNormalClose;
                    }
                    else
                    {
                        return errorCode.Value;
                    }
                case ProtocolDatagram.OpCodeEnquireLinkAck:
                    if (errorCode == null || errorCode < 100 || errorCode >= 200)
                    {
                        return ErrorCodeSessionOk;
                    }
                    else
                    {
                        return errorCode.Value;
                    }
                case ProtocolDatagram.OpCodeDataAck:
                    if (errorCode == null || errorCode < 200 || errorCode >= 300)
                    {
                        // no default in this case.
                        return 0;
                    }
                    else
                    {
                        return errorCode.Value;
                    }
                default:
                    break;
            }
            return int.MinValue;
        }

        public static string StringifyReason(int errorCode)
        {
            string reasonPhrase = FormatErrorCode(errorCode);
            bool causedByRemotePeer = errorCode < 0;
            string suffix = "Caused by " + (causedByRemotePeer ? "remote" : "local") + " peer";
            return reasonPhrase + " " + suffix;
        }

        public static string FormatErrorCode(int code)
        {
            if (code == 0)
                return "NONE";
            if (code == ErrorCodeNormalClose)
                return "NORMAL CLOSE";
            else if (code == ErrorCodeOpenTimeout)
                return "OPEN_TIMEOUT";
            else if (code == ErrorCodeIdleTimeout)
                return "IDLE_TIMEOUT";
            else if (code == ErrorCodeSendTimeout)
                return "SEND_TIMEOUT";
            else if (code == ErrorCodeForceClose)
                return "FORCED CLOSE";
            else if (code == ErrorCodeInternalError)
                return "INTERNAL ERROR";
            else if (code == ErrorCodeApplicationError)
                return "APPLICATION ERROR";
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

        public ProtocolOperationException(int errorCode):
            base(StringifyReason(errorCode))
        {
            ErrorCode = errorCode;
        }

        public ProtocolOperationException(Exception innerException):
            this(ErrorCodeApplicationError, StringifyReason(ErrorCodeApplicationError),
                innerException)
        { }

        public ProtocolOperationException(int errorCode, Exception innerException) :
            base(StringifyReason(errorCode), innerException)
        {
            ErrorCode = errorCode;
        }

        public ProtocolOperationException(int errorCode, string message,
                Exception innerException):
            base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; }
    }
}
