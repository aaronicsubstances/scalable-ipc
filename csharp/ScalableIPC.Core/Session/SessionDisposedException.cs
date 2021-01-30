using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SessionDisposedException: Exception
    {
        private static string StringifyReason(bool fromRemote, int abortCode)
        {
            string reasonPhrase = ProtocolDatagram.FormatAbortCode(abortCode);
            string suffix = fromRemote ? "by remote peer" : "by local peer";
            return reasonPhrase + " " + suffix;
        }

        public SessionDisposedException(bool fromRemote, int abortCode):
            base(StringifyReason(fromRemote, abortCode))
        {
            FromRemote = fromRemote;
            AbortCode = abortCode;
        }

        public SessionDisposedException(Exception innerException):
            this(false, ProtocolDatagram.AbortCodeError, StringifyReason(false, ProtocolDatagram.AbortCodeError), innerException)
        { }

        public SessionDisposedException(bool fromRemote, int reason, string message, Exception innerException):
            base(message, innerException)
        {
            FromRemote = fromRemote;
            AbortCode = reason;
        }

        public bool FromRemote { get; }

        public int AbortCode { get; }
    }
}
