using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session;
using System;

namespace ScalableIPC.Tests.Core.Transports.Test
{
    class TestSessionHandler : SessionHandlerBase
    {
        public override void OnDataReceived(byte[] windowData, ProtocolDatagramOptions windowOptions)
        {
            string dataMessage = ProtocolDatagram.ConvertBytesToString(windowData, 0, windowData.Length);
            CustomLoggerFacade.Log(() => new CustomLogEvent("71931970-3923-4472-b110-3449141998e3",
                $"Received data: {dataMessage}", null));
        }

        public override void OnClose(SessionCloseException cause)
        {
            CustomLoggerFacade.Log(() => new CustomLogEvent("06f62330-a218-4667-9df5-b8851fed628a",
                   $"Received close", cause));
        }
    }
}
