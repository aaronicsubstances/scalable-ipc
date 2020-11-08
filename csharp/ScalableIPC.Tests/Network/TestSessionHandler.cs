using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Tests.Network
{
    class TestSessionHandler : ProtocolSessionHandler
    {
        public override void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options, bool isLastOpenRequest)
        {
            base.OnOpenRequest(data, options, isLastOpenRequest);
            string openMessage = ProtocolDatagram.ConvertBytesToString(data, 0, data.Length);
            CustomLoggerFacade.Log(() => new CustomLogEvent("52d5d77c-9ee8-4e76-a435-4aca4bf05ab7", 
                $"Received open request message: {openMessage}", (Exception) null));
        }
        public override void OnDataReceived(byte[] data, Dictionary<string, List<string>> options)
        {
            base.OnDataReceived(data, options);
            string dataMessage = ProtocolDatagram.ConvertBytesToString(data, 0, data.Length);
            CustomLoggerFacade.Log(() => new CustomLogEvent("71931970-3923-4472-b110-3449141998e3",
                $"Received data: {dataMessage}", (Exception)null));
        }
        public override void OnClose(Exception error, bool timeout)
        {
            base.OnClose(error, timeout);
        }
    }
}