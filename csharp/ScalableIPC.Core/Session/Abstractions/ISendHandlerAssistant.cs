﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface ISendHandlerAssistant
    {
        List<ProtocolDatagram> CurrentWindow { get; set; }
        int PreviousSendCount { get; set; }

        /// <summary>
        /// Used to alternate between stop and wait flow control, and go back N in between timeouts. 
        /// </summary>
        bool StopAndWait { get; set; }
        int AckTimeoutSecs { get; set; }
        Action SuccessCallback { get; set; }
        Action<SessionDisposedException> DisposeCallback { get; set; }
        Action TimeoutCallback { get; set; }
        bool IsComplete { get; }

        void Start();
        void OnAckReceived(ProtocolDatagram datagram);
        void Cancel();
    }
}