using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    /// <summary>
    /// Implementation is similar to go-back-N on the side of sender, with some important differences:
    /// <list type="number">
    /// <item>transmit window size changes from N to 1 during retries.</item>
    /// <item>sequence number limits and window number limits are defined separately 
    /// rather than jointly</item>
    /// <item>retries always start from the beginning. no assumption is made at the start of a retry
    /// about which datagrams have arrived at receiver's end.</item>
    /// <item>retries leverage received acks to skip unnecessary sending of some packets.</item>
    /// </list>
    /// </summary>
    public interface ISendHandlerAssistant
    {
        List<ProtocolDatagram> CurrentWindow { get; set; }

        /// <summary>
        /// Used to alternate between using send window size as it is, or reducing it to 1 if property is true.
        /// </summary>
        bool StopAndWait { get; set; }
        int RetryCount { get; set;  }
        Action SuccessCallback { get; set; }
        Action<ProtocolOperationException> ErrorCallback { get; set; }
        Action<int> WindowFullCallback { get; set; }
        Action TimeoutCallback { get; set; }
        bool IsStarted { get; }
        bool IsComplete { get; }
        int SentCount { get; }
        object SendContext { get; }

        void Start();
        void OnAckReceived(ProtocolDatagram datagram);
        void Cancel();
    }
}
