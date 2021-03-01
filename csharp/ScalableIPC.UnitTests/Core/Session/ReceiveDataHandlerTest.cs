using ScalableIPC.Core;
using ScalableIPC.Core.Session;
using ScalableIPC.UnitTests.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.UnitTests.Core.Session
{
    public class ReceiveDataHandlerTest
    {
        [Theory]
        [MemberData(nameof(CreateTestGetLastPositionInSlidingWindowData))]
        public void TestGetLastPositionInSlidingWindow(List<ProtocolDatagram> currentWindow, int expected)
        {
            int actual = ReceiveDataHandler.GetLastPositionInSlidingWindow(currentWindow);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestGetLastPositionInSlidingWindowData()
        {
            return new List<object[]>
            {
                new object[]{ new List<ProtocolDatagram>(), -1 },
                new object[]{ new List<ProtocolDatagram> { null, new ProtocolDatagram() }, -1 },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    new ProtocolDatagram() }, 2 },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    null, new ProtocolDatagram() }, 1 },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram() }, 0 },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestIsCurrentWindowFullData))]
        public void TestIsCurrentWindowFull(List<ProtocolDatagram> currentWindow, int maxReceiveWindowSize,
            int lastPosInSlidingWindow, bool expected)
        {
            bool actual = ReceiveDataHandler.IsCurrentWindowFull(currentWindow, maxReceiveWindowSize,
                lastPosInSlidingWindow);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestIsCurrentWindowFullData()
        {
            return new List<object[]>
            {
                new object[]{ new List<ProtocolDatagram>(), 0, -1, false },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram() }, 0, 0, true },
                new object[]{ new List<ProtocolDatagram>(), 1, -1, false },
                new object[]{ new List<ProtocolDatagram> { null, new ProtocolDatagram() }, 2, -1, false },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    new ProtocolDatagram() }, 3, 2, true },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    new ProtocolDatagram() }, 4, 2, false },
                new object[]
                {
                    new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                        new ProtocolDatagram
                        {
                            Options = new ProtocolDatagramOptions { IsLastInWindow = true } 
                        } 
                    }, 
                    4, 2, true
                },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    null, new ProtocolDatagram() }, 2, 1, true },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram(), new ProtocolDatagram(),
                    null, new ProtocolDatagram() }, 3, 1, false },
                new object[]
                { 
                    new List<ProtocolDatagram> 
                    {
                        new ProtocolDatagram
                        {
                            Options = new ProtocolDatagramOptions { IsLastInWindow = true } 
                        }, 
                        new ProtocolDatagram(), null, new ProtocolDatagram() 
                    }, 3, 0, true 
                },
                new object[]{ new List<ProtocolDatagram> { new ProtocolDatagram() }, 1, 0, true },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestAddToCurrentWindowData))]
        public void TestAddToCurrentWindow(List<ProtocolDatagram> inputWindow, int maxReceiveWindowSize,
            ProtocolDatagram message, bool expected, List<ProtocolDatagram> expectedWindow)
        {
            var mutableWindow = new List<ProtocolDatagram>(inputWindow);
            bool actual = ReceiveDataHandler.AddToCurrentWindow(mutableWindow, maxReceiveWindowSize,
                message);
            Assert.Equal(expected, actual);
            if (!expected && expectedWindow == null)
            {
                expectedWindow = inputWindow;
            }
            if (maxReceiveWindowSize < 1)
            {
                Assert.True(mutableWindow.Count == 1,
                    $"Expected {mutableWindow.Count} == 1");
            }
            else
            {
                Assert.True(mutableWindow.Count <= maxReceiveWindowSize,
                    $"Expected {mutableWindow.Count} <= {maxReceiveWindowSize}");
            }
            Assert.Equal(expectedWindow, mutableWindow, new ProtocolDatagramComparer());
        }

        public static List<object[]> CreateTestAddToCurrentWindowData()
        {
            // Possibilities:
            // 1. different window id, smaller, larger
            // 2. seq number too large.
            // 3. message has last window id, and is the first
            // 4. message has last window id, and is not the first, and comes after.
            // 5. message has last window id, and is not the first, and comes before.

            var testArgs = new List<object[]>();

            // use intentional scopes to scope variables.

            // very first.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>();
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram();
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // maxReceiveWindowSize = 0: non-positives should be treated as equivalent to 1.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>();
                int maxReceiveWindowSize = 0;
                ProtocolDatagram message = new ProtocolDatagram();
                bool expected = true;
                var expectedWindow = new List<ProtocolDatagram>
                {
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // seq num too large
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram()
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 100 };
                bool expected = false;
                List<ProtocolDatagram> expectedWindow = null;
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram(),
                    null
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 10 };
                bool expected = false;
                List<ProtocolDatagram> expectedWindow = null;
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram(),
                    new ProtocolDatagram { SequenceNumber = 1 },
                    new ProtocolDatagram { SequenceNumber = 2 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 11 };
                bool expected = false;
                List<ProtocolDatagram> expectedWindow = null;
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // seq ok
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>();
                int maxReceiveWindowSize = 3;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 2 };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    null,
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // new window id less than current window id
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 1 };
                bool expected = false;
                List<ProtocolDatagram> expectedWindow = null;
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // new window id greater than current window id
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 1, WindowId = 4 };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // new window id = current window id
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 1, WindowId = 3 };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 },
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 0, WindowId = 3 };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    null,
                    new ProtocolDatagram { SequenceNumber = 1, WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram { SequenceNumber = 0, WindowId = 3 };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    message,
                    new ProtocolDatagram { SequenceNumber = 1, WindowId = 3 }
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // lastInWindow received, and is very first.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 2,
                    WindowId = 3,
                    Options = new ProtocolDatagramOptions
                    { 
                        IsLastInWindow = true 
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { WindowId = 3 },
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>();
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 0,
                    WindowId = 3,
                    Options = new ProtocolDatagramOptions
                    { 
                        IsLastInWindow = true 
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // lastInWindow received, and comes after existing lastInWindow.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram(),
                    new ProtocolDatagram
                    { 
                        SequenceNumber = 1,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 2,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    inputWindow[0],
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram(),
                    new ProtocolDatagram
                    {
                        SequenceNumber = 1,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 5,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    inputWindow[0],
                    null,
                    null,
                    null,
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram
                    {
                        SequenceNumber = 0,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 6;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 5,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    null,
                    null,
                    null,
                    null,
                    null,
                    message
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // lastInWindow received, and comes before existing lastInWindow.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    null,
                    new ProtocolDatagram
                    {
                        SequenceNumber = 1,
                        WindowId = 3,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    },
                    new ProtocolDatagram
                    {
                        SequenceNumber = 2, 
                        WindowId = 3,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 0,
                    WindowId = 3,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    message,
                    null,
                    null
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // another of previous
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    null,
                    new ProtocolDatagram { SequenceNumber = 1, WindowId = 3 },
                    new ProtocolDatagram { SequenceNumber = 2, WindowId = 3 },
                    new ProtocolDatagram
                    {
                        SequenceNumber = 3, 
                        WindowId = 3,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 1,
                    WindowId = 3,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    null,
                    message,
                    null,
                    null
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            // lastInWindow received, and none exists previously, but sequence number doesn't place
            // it last.
            {
                List<ProtocolDatagram> inputWindow = new List<ProtocolDatagram>
                {
                    new ProtocolDatagram { SequenceNumber = 0, WindowId = 3 },
                    new ProtocolDatagram { SequenceNumber = 1, WindowId = 3 },
                    new ProtocolDatagram
                    {
                        SequenceNumber = 2,
                        WindowId = 3,
                        Options = new ProtocolDatagramOptions
                        {
                            IsLastInWindow = true
                        }
                    }
                };
                int maxReceiveWindowSize = 10;
                ProtocolDatagram message = new ProtocolDatagram
                {
                    SequenceNumber = 1,
                    WindowId = 3,
                    Options = new ProtocolDatagramOptions
                    {
                        IsLastInWindow = true
                    }
                };
                bool expected = true;
                List<ProtocolDatagram> expectedWindow = new List<ProtocolDatagram>
                {
                    inputWindow[0],
                    message,
                    null
                };
                testArgs.Add(new object[] { inputWindow, maxReceiveWindowSize, message, expected, expectedWindow });
            }

            return testArgs;
        }
    }
}
