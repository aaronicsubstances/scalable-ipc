using ScalableIPC.Core;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Session
{
    public class ReceiveHandlerAssistantTest
    {
        [Theory]
        [MemberData(nameof(CreateTestGetLastPositionInSlidingWindowData))]
        public void TestGetLastPositionInSlidingWindow(List<ProtocolDatagram> currentWindow, int expected)
        {
            int actual = ReceiveHandlerAssistant.GetLastPositionInSlidingWindow(currentWindow);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestGetLastPositionInSlidingWindowData()
        {
            return new List<object[]>
            {
                new object[]{ new List<ProtocolDatagram>(), -1 },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestIsCurrentWindowFullData))]
        public void TestIsCurrentWindowFull(List<ProtocolDatagram> currentWindow, int maxReceiveWindowSize,
            int lastPosInSlidingWindow, bool expected)
        {
            bool actual = ReceiveHandlerAssistant.IsCurrentWindowFull(currentWindow, maxReceiveWindowSize,
                lastPosInSlidingWindow);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestIsCurrentWindowFullData()
        {
            return new List<object[]>
            {
                new object[]{ new List<ProtocolDatagram>(), 10, -1, false },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestAddToCurrentWindowData))]
        public void TestAddToCurrentWindow(List<ProtocolDatagram> currentWindow, int maxReceiveWindowSize,
            ProtocolDatagram message, bool expected)
        {
            bool actual = ReceiveHandlerAssistant.AddToCurrentWindow(currentWindow, maxReceiveWindowSize,
                message);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestAddToCurrentWindowData()
        {
            return new List<object[]>
            {
                new object[]{ new List<ProtocolDatagram>(), 10, new ProtocolDatagram { WindowId = 1, SequenceNumber = 2 }, true },
            };
        }
    }
}
