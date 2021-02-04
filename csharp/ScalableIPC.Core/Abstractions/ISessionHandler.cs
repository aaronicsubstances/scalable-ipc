using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Session handler is meant to is to hide acks, retries, window ids and
    /// sequence numbers from application layer. It incorporates 80% of data link layer design and
    /// 20% of transport design.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifically, these are the features:
    /// </para>
    /// <list type="number">
    /// <item>end to end assumption of communication endpoints</item>
    /// <item>end to end idle timeout specification</item>
    /// <item>packet integrity assumption</item>
    /// <item>guaranteed delivery via acknowlegements</item>
    /// <item>deal with out of order packet delivery</item>
    /// <item>deal with packet duplication - like in TCP</item>
    /// <item>retries upon timeouts via ARQ - go back N variant (designed for transport layer) 
    /// on sender side, selective repeat on receiver size.</item>
    /// <item>all send errors are transient by default. Fatal errors are the exception.</item>
    /// <item>flow control</item>
    /// <item>preservation of message boundaries</item>
    /// <item>special startup and shutdown (designed for transport layer)</item>
    /// <item>DOES NOT deal with congestion control and security</item>
    /// <item>deals only with unicast communications</item>
    /// </list>
    /// </remarks>
    public interface ISessionHandler
    {
        void CompleteInit(string sessionId, bool configureForSendOpen,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint);
        AbstractNetworkApi NetworkApi { get; }
        GenericNetworkIdentifier RemoteEndpoint { get; }
        string SessionId { get; }
        bool ConfiguredForSendOpen { get; }

        AbstractPromise<VoidType> ProcessOpenAsync();
        AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram);
        AbstractPromise<VoidType> SendAsync(ProtocolMessage message);
        AbstractPromise<bool> SendWithoutAckAsync(ProtocolMessage message);
        AbstractPromise<VoidType> CloseAsync();
        AbstractPromise<VoidType> CloseAsync(int errorCode);
        AbstractPromise<VoidType> FinaliseDisposeAsync(ProtocolOperationException cause);
        int IdleTimeout { get; set; } // non-positive means disable idle timer 
        int MinRemoteIdleTimeout { get; set; }
        int MaxRemoteIdleTimeout { get; set; }
        int MaxWindowSize { get; set; } // non-positive means use 1.
        int MaxRetryCount { get; set; } // negative means use 0.
                                        
        // ranges from 0 to 1. non-positive means always ignore. 
        // 1 or more means always send.
        double FireAndForgetSendProbability { get; set; }
        int EnquireLinkInterval { get; set; } // non-positive means disable enquire link timer
        Func<int, int> EnquireLinkIntervalAlgorithm { get; set; }

        // application layer interface. contract here is that these should be scheduled on event loop.
        Action<ISessionHandler, ProtocolDatagram> DatagramDiscardedHandler { get; set; }
        Action<ISessionHandler> OpenSuccessHandler { get; set; }
        Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }
        Action<ISessionHandler, int> EnquireLinkTimerFiredHandler { get; set; }
    }

    public interface ISessionHandlerFactory
    {
        ISessionHandler Create();
    }
}
