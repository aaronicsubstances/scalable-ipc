using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolOperationException: Exception
    {
        // Normal protocol ack error codes
        public const int ErrorCodeInvalidDestinationEndpointId = 1;
        public const int ErrorCodeMessageTooLarge = 2;
        public const int ErrorCodeOutOfBufferSpace = 3;
        public const int ErrorCodeReceiveBufferOverflow = 4;

        // The following error codes are not meant to be used for network
        // communications. As such they are negative.
        public const int ErrorCodeReset = -1;
        public const int ErrorCodeShutdown = -2;
        public const int ErrorCodeSendTimeout = -3;
        public const int ErrorCodeReceiveTimeout = -4;
        public const int ErrorCodeApplicationError = -5;

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
            else if (code == ErrorCodeReceiveBufferOverflow)
                return "RECEIVE_BUFFER_OVERFLOW";
            else if (code == ErrorCodeMessageTooLarge)
                return "MESSAGE_TOO_LARGE";
            else if (code == ErrorCodeOutOfBufferSpace)
                return "OUT_OF_BUFFER_SPACE";
            else if (code == ErrorCodeReset)
                return "RESET";
            else if (code == ErrorCodeSendTimeout)
                return "SEND_TIMEOUT";
            else if (code == ErrorCodeShutdown)
                return "SHUTDOWN";
            else if (code == ErrorCodeReceiveTimeout)
                return "RECEIVE_TIMEOUT";
            else if (code == ErrorCodeApplicationError)
                return "APPLICATION_ERROR";
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
