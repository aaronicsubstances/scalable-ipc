﻿using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public interface IReferenceSessionHandler: ISessionHandler
    {
        int SessionState { get; set; }

        // Rules for window id changes are:
        //  - First window id must be 0. 
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that after 9E18, receiver must receive a next starting from 1
        //  - In any case increments must be less than one thousand (1000).
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        int LastMaxSeqReceived { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();
        void PostIfNotDisposed(Action cb);

        int? RemoteIdleTimeoutSecs { get; set; }

        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void CancelAckTimeout();
        void DiscardReceivedDatagram(ProtocolDatagram datagram);
        void InitiateDispose(SessionDisposedException cause, PromiseCompletionSource<VoidType> promiseCb);
        void ContinueDispose(SessionDisposedException cause);
        void Log(string logPosition, string message, params object[] args);
        void Log(string logPosition, ProtocolDatagram datagram, string message, params object[] args);

        // application layer interface. contract here is that these should be called from event loop.
        event EventHandler<MessageReceivedEventArgs> MessageReceived;
        event EventHandler<SessionDisposedEventArgs> SessionDisposed;
        void OnMessageReceived(MessageReceivedEventArgs e);
        void OnSessionDisposed(SessionDisposedEventArgs e);
    }
}