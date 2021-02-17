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
    /// <item>assumption that session ids are not reusable.</item>
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
        void CompleteInit(string sessionId, AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint);
        AbstractNetworkApi NetworkApi { get; }
        GenericNetworkIdentifier RemoteEndpoint { get; }
        string SessionId { get; }
        AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram);
        AbstractPromise<VoidType> SendAsync(ProtocolMessage message);
        AbstractPromise<VoidType> CloseAsync();
        AbstractPromise<VoidType> CloseAsync(int errorCode);
        AbstractPromise<VoidType> FinaliseDisposeAsync(ProtocolOperationException cause);
        int OpenTimeout { get; set; } // equivalent to idle timeout prior to successful opening of session.
        int IdleTimeout { get; set; } // non-positive means disable idle timer
        int AckTimeout { get; set; }
        int MinRemoteIdleTimeout { get; set; }
        int MaxRemoteIdleTimeout { get; set; }
        int MaxWindowSize { get; set; } // non-positive means use 1.
        int MaxRetryCount { get; set; } // negative means use 0.
        int EnquireLinkInterval { get; set; } // non-positive means disable enquire link timer
        Func<int, int> EnquireLinkIntervalAlgorithm { get; set; }

        // Rules for window id changes are:
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that after 9E15, receiver must receive a next starting from 0
        //  - In any case increments cannot exceed 100.
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        Func<long, long> NextWindowIdToSendAlgorithm { get; set; }

        // application layer interface. contract here is that these should be scheduled on event loop.
        Action<ISessionHandler, ProtocolDatagram> DatagramDiscardedHandler { get; set; }
        Action<ISessionHandler, bool> OpenSuccessHandler { get; set; }
        Action<ISessionHandler, ReceivedProtocolMessage> MessageReceivedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }
        Action<ISessionHandler, int> EnquireLinkTimerFiredHandler { get; set; }
        Action<ISessionHandler, ProtocolDatagram> EnquireLinkSuccessHandler { get; set; }
    }

    public interface ISessionHandlerFactory
    {
        ISessionHandler Create();
    }
}
