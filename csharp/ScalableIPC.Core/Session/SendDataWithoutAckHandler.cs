using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataWithoutAckHandler: ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private readonly Random _randGen = new Random();
        private PromiseCompletionSource<bool> _pendingPromiseCallback;
        private IFireAndForgetSendHandlerAssistant _fireAndForgetHandler;

        public SendDataWithoutAckHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress { get; set; }

        public void Dispose(ProtocolOperationException cause)
        {
            _fireAndForgetHandler?.Cancel();
            _fireAndForgetHandler = null;
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                _pendingPromiseCallback.CompleteExceptionally(cause);
                _pendingPromiseCallback = null;
            }
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeDataAck)
            {
                return false;
            }

            // to prevent clashes with other handlers performing sends, 
            // check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            // we don't do anything with acks here, but still indicate that 
            // they have been used to prevent further processing.
            return true;
        }

        public void ProcessSendWithoutAck(ProtocolMessage message,
           PromiseCompletionSource<bool> promiseCb)
        {
            // ensure minimum of 512 and maximum = datagram max length
            int mtu = Math.Min(Math.Max(ProtocolDatagram.MinimumTransferUnitSize,
                _sessionHandler.NetworkApi.MaximumTransferUnitSize), ProtocolDatagram.MaxDatagramSize);

            // must generate only 1 datagram of mtu size max.
            bool mtuExceeded = false;
            var datagramFragmenter = new ProtocolDatagramFragmenter(message, mtu, null);
            var firstWindowGroup = datagramFragmenter.Next();
            if (firstWindowGroup.Count == 0)
            {
                throw new Exception("Wrong fragmentation algorithm. At least one datagram must be returned");
            }
            if (firstWindowGroup.Count > 1)
            {
                mtuExceeded = true;
            }
            if (datagramFragmenter.Next().Count > 0)
            {
                mtuExceeded = true;
            }
            if (mtuExceeded)
            {
                promiseCb.CompleteExceptionally(new Exception("Can only send 1 datagram of size MTU" +
                    "with fire and forget"));
                return;
            }

            var datagram = firstWindowGroup[0];
            datagram.OpCode = ProtocolDatagram.OpCodeData;
            datagram.SessionId = _sessionHandler.SessionId;
            // the rest will be set by assistant handlers

            // we may still not need to send depending on chance
            if (_randGen.NextDouble() >= _sessionHandler.FireAndForgetSendProbability)
            {
                promiseCb.CompleteSuccessfully(false);
                return;
            }

            _fireAndForgetHandler = _sessionHandler.CreateFireAndForgetSendHandlerAssistant();
            _fireAndForgetHandler.MessageToSend = datagram;
            _fireAndForgetHandler.SuccessCallback = OnSendSuccess;
            _fireAndForgetHandler.ErrorCallback = OnSendFailure;
            _fireAndForgetHandler.Start();

            SendInProgress = true;
            _pendingPromiseCallback = promiseCb;
        }

        private void OnSendSuccess()
        {
            SendInProgress = false;
            _pendingPromiseCallback.CompleteSuccessfully(true);
            _pendingPromiseCallback = null;
            _fireAndForgetHandler = null;
        }

        private void OnSendFailure(ProtocolOperationException error)
        {
            SendInProgress = false;

            _pendingPromiseCallback.CompleteSuccessfully(false);
            _pendingPromiseCallback = null;
            _fireAndForgetHandler = null;
            _sessionHandler.OnSendError(error);
        }
    }
}
