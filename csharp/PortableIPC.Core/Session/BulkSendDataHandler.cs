using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class BulkSendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly SendDataHandler _sendHandler;
        private readonly AbstractPromiseApi _promiseApi;

        private AbstractPromiseCallback<VoidType> _pendingPromiseCallback;
        private byte[] _rawData;
        private int _offset;

        public BulkSendDataHandler(ISessionHandler sessionHandler, SendDataHandler sendHandler)
        {
            _sessionHandler = sessionHandler;
            _sendHandler = sendHandler;
            _promiseApi = _sessionHandler.EndpointHandler.PromiseApi;
        }

        public void Shutdown(Exception error)
        {
            _pendingPromiseCallback?.CompleteExceptionally(error);
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] rawData, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != SessionState.OpenedForData)
            {
                return false;
            }
            if (opCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }
            if (_sendHandler.SendInProgress)
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return true;
            }
            // if entire message will fit into 1 PDU, just delegate to sendHandler directly.
            if (rawData.Length <= _sessionHandler.MaxPduSize)
            {
                var message = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    DataBytes = rawData,
                    DataLength = rawData.Length
                };
                return _sendHandler.ProcessSend(message, promiseCb);
            }

            // So raw bytes will need multiple PDUs.
            _pendingPromiseCallback = promiseCb;
            _rawData = rawData;
            _offset = 0;
            ContinueBulkSend();

            return true;
        }

        private void ContinueBulkSend()
        {
            var nextWindow = new List<ProtocolDatagram>();
            int seqGen = _sessionHandler.NextSendSeqStart;
            while (_offset < _rawData.Length && nextWindow.Count < _sessionHandler.WindowSize)
            {
                var messagePart = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    SequenceNumber = seqGen++,
                    DataBytes = _rawData,
                    DataOffset = _offset,
                    DataLength = Math.Min(_sessionHandler.MaxPduSize, _rawData.Length - _offset)
                };
                nextWindow.Add(messagePart);
                _offset += _sessionHandler.MaxPduSize;
            }
            if (nextWindow.Count < _sessionHandler.WindowSize)
            {
                nextWindow[nextWindow.Count - 1].IsLastInWindow = true;
            }
            _sendHandler.CurrentWindow.Clear();
            _sendHandler.CurrentWindow.AddRange(nextWindow);
            _sendHandler.SendInProgress = true;
            AbstractPromiseCallback<VoidType> partPromiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> partPromise = partPromiseCb.Extract();
            _sendHandler.ProcessSendWindow(partPromiseCb);
            // let send handler handle error, which will lead to a closure of this handler,
            // and then _pendingPromiseCallback can be completed with error.
            partPromise.Then(HandleSendPartSuccess);
        }

        private VoidType HandleSendPartSuccess(VoidType _)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                if (_offset < _rawData.Length)
                {
                    ContinueBulkSend();
                }
                else
                {
                    // Done.
                    _sendHandler.SendInProgress = false;
                    _rawData = null;
                    _sendHandler.CurrentWindow.Clear();

                    _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
                }
            });
            return VoidType.Instance;
        }
    }
}
