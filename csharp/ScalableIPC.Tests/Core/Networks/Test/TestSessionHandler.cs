using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session;
using System;

namespace ScalableIPC.Tests.Core.Networks.Test
{
    class TestSessionHandler : DefaultSessionHandler
    {
        public TestSessionHandler()
        {

            MessageReceived += (_, e) =>
            {
                string dataMessage = ProtocolDatagram.ConvertBytesToString(e.Message.DataBytes, e.Message.DataOffset,
                    e.Message.DataLength);
                CustomLoggerFacade.Log(() => new CustomLogEvent("71931970-3923-4472-b110-3449141998e3",
                    $"Received data: {dataMessage}", null));
            };

            SessionDisposed += (_, e) =>
            {
                CustomLoggerFacade.Log(() => new CustomLogEvent("06f62330-a218-4667-9df5-b8851fed628a",
                       $"Received close", e.Cause));
            };
        }
    }
}
