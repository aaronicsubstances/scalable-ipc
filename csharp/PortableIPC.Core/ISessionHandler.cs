using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public interface ISessionHandler
    {
        // beginning of public API.
        ProtocolEndpointHandler EndpointHandler { get; set; }
        IPEndPoint ConnectedEndpoint { get; set; }
        string SessionId { get; set; }
        List<ISessionStateHandler> StateHandlers { get; }
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSendData(byte[] rawData);
        AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message);
        AbstractPromise<VoidType> Close(Exception error, bool timeout);
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

        void ResetAckTimeout(int timeoutSecs, StoredCallback cb);
        void DiscardReceivedMessage(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        void HandleClosing(Exception error, bool timeout, AbstractPromiseCallback<VoidType> promiseCb);

        // application layer interface
        void OnOpenReceived(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        void OnDataReceived(byte[] data, int offset, int length, AbstractPromiseCallback<VoidType> promiseCb);
        void OnClose(Exception error, bool timeout, AbstractPromiseCallback<VoidType> promiseCb);
    }
}
