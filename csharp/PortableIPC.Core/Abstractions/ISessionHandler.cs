﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        // beginning of public API.
        IEndpointHandler EndpointHandler { get; set; }
        IPEndPoint ConnectedEndpoint { get; set; }
        string SessionId { get; set; }
        List<ISessionStateHandler> StateHandlers { get; }
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSendData(byte[] rawData);
        AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message);
        AbstractPromise<VoidType> Shutdown(Exception error, bool timeout);
        AbstractPromise<VoidType> ProcessErrorReceive();

        // beginning of internal API with state handlers.
        bool IsOpened { get; set; }
        bool IsClosed { get; }
        int MaxPduSize { get; set; }
        int MaxRetryCount { get; set; }
        int WindowSize { get; set; }
        int IdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; }
        void PostSerially(Action cb);
        void PostSeriallyIfNotClosed(Action cb);

        void EnsureIdleTimeout();
        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void DiscardReceivedMessage(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        void ProcessShutdown(Exception error, bool timeout);

        // application layer interface
        void OnOpenReceived(ProtocolDatagram message);
        void OnDataReceived(byte[] data, int offset, int length);
        void OnClose(Exception error, bool timeout);
    }
}
