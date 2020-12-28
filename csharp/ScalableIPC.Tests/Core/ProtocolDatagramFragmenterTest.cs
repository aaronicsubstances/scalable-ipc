using ScalableIPC.Core;
using ScalableIPC.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Core
{
    public class ProtocolDatagramFragmenterTest
    {
        // helper method to create without intermediate variables.
        private static ProtocolDatagram AddOptions(ProtocolDatagram instance, Dictionary<string, List<string>> options)
        {
            foreach (var kvp in options)
            {
                if (instance.Options == null)
                {
                    instance.Options = new ProtocolDatagramOptions();
                }
                instance.Options.AllOptions.Add(kvp.Key, kvp.Value);
            }
            return instance;
        }

        [Theory]
        [MemberData(nameof(CreateTestEncodeLongOptionData))]
        public void TestEncodeLongOption(string name, string value, int maxFragByteCount, int spaceToConsiderForFirst,
            List<string> expected)
        {
            var actual = ProtocolDatagramFragmenter.EncodeLongOption(name, value, maxFragByteCount, spaceToConsiderForFirst);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestEncodeLongOptionData()
        {
            var testData = new List<object[]>();

            string name = "protocol";
            string value = "http";
            int maxFragByteCount = 2;
            List<string> expected = new List<string>
            {
                "8:", "pr", "ot", "oc", "ol", "ht", "tp"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "protocol";
            value = "ftp";
            maxFragByteCount = 2;
            expected = new List<string>
            {
                "8:", "pr", "ot", "oc", "ol", "ft", "p"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "";
            value = "";
            maxFragByteCount = 2;
            expected = new List<string>
            {
                "0:"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "abcdefghij";
            value = "";
            maxFragByteCount = 1;
            expected = new List<string>
            {
                "1", "0", ":", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "";
            value = "abcdefghijkl";
            maxFragByteCount = 3;
            expected = new List<string>
            {
                "0:a", "bcd", "efg", "hij", "kl"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            // test with non-ASCII chars involved.
            name = "poweredby";
            value = "as\u025bmpa";
            maxFragByteCount = 3;
            expected = new List<string>
            {
                "9:p", "owe", "red", "bya", "s\u00C9", "\u009Bm", "pa"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "poweredby";
            value = "as\u025b";
            maxFragByteCount = 3;
            expected = new List<string>
            {
                "9:p", "owe", "red", "bya", "s\u00C9", "\u009B"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "powered-by";
            value = "ohas\u025bmpa";
            maxFragByteCount = 3;
            expected = new List<string>
            {
                "10:", "pow", "ere", "d-b", "yoh", "as", "\u00C9", "\u009Bm", "pa"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "\u0254";
            value = "ohas\u025bmpas";
            maxFragByteCount = 3;
            expected = new List<string>
            {
                "1:", "\u00C9", "\u0094o", "has", "\u00C9", "\u009Bm", "pas"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "\u0254\u0254";
            value = "ohas\u025b\u025bmpas";
            maxFragByteCount = 2;
            expected = new List<string>
            {
                "2:", "\u00C9", "\u0094", "\u00C9", "\u0094", "oh", "as",
                "\u00C9", "\u009B", "\u00C9", "\u009B", "mp", "as"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, -1, expected });

            name = "rainy-months";
            value = "june-july";
            maxFragByteCount = 4;
            expected = new List<string>
            {
                "12", ":rai", "ny-m", "onth",
                "sjun", "e-ju", "ly"
            };
            testData.Add(new object[] { name, value, maxFragByteCount, 2, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateEncodeLongOptionWithErrorData))]
        public void TestEncodeLongOptionWithError(string name, string value, int maxFragByteCount)
        {
            Assert.ThrowsAny<Exception>(() => ProtocolDatagramFragmenter.EncodeLongOption(name, value, maxFragByteCount, -1));
        }

        public static List<object[]> CreateEncodeLongOptionWithErrorData()
        {
            var testData = new List<object[]>();

            string name = "protocol";
            string value = "http";
            int maxFragByteCount = 0;
            testData.Add(new object[] { name, value, maxFragByteCount });

            name = "\u0254";
            value = "ftp";
            maxFragByteCount = 1;
            testData.Add(new object[] { name, value, maxFragByteCount });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestDecodeLongOptionData))]
        public void TestDecodeLongOption(List<string> values, string[] expected)
        {
            var actual = ProtocolDatagramFragmenter.DecodeLongOption(values);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDecodeLongOptionData()
        {
            var testData = new List<object[]>();

            List<string> values = new List<string>
            {
                "8:", "pr", "ot", "oc", "ol", "ht", "tp"
            };
            string[] expected = { "protocol", "http" };
            testData.Add(new object[] { values, expected });

            values = new List<string>
            {
                "8:protocolhttp"
            };
            expected = new string[] { "protocol", "http" };
            testData.Add(new object[] { values, expected });

            values = new List<string>
            {
                "0:"
            };
            expected = new string[] { "", "" };
            testData.Add(new object[] { values, expected });

            values = new List<string>
            {
                "1", "0", ":", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j"
            };
            expected = new string[] { "abcdefghij", "" };
            testData.Add(new object[] { values, expected });

            values = new List<string>
            {
                "0:a", "bcd", "efg", "hij", "kl"
            };
            expected = new string[] { "", "abcdefghijkl" };
            testData.Add(new object[] { values, expected });

            values = new List<string>
            {
                "0:a", "bcdefg", "hij", "kl"
            };
            expected = new string[] { "", "abcdefghijkl" };
            testData.Add(new object[] { values, expected });

            // test with non-ASCII chars involved.
            values = new List<string>
            {
                "2:", "\u00C9", "\u0094", "\u00C9", "\u0094", "oh", "as",
                "\u00C9", "\u009B", "\u00C9", "\u009B", "mp", "as"
            };
            expected = new string[] { "\u0254\u0254", "ohas\u025b\u025bmpas" };
            testData.Add(new object[] { values, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateDecodeLongOptionWithErrorData))]
        public void TestDecodeLongOptionWithError(List<string> values)
        {
            Assert.ThrowsAny<Exception>(() => ProtocolDatagramFragmenter.DecodeLongOption(values));
        }

        public static List<object[]> CreateDecodeLongOptionWithErrorData()
        {
            return new List<object[]>
            {
                new object[]{ new List<string> { } },
                new object[]{ new List<string> { "nocolon" } },
                new object[]{ new List<string> { ":nolength" } },
                new object[]{ new List<string> { "wronglength:", "sth" } },
                new object[]{ new List<string> { "a:", "sth" } },
                new object[]{ new List<string> { "13:", "sth" } },
                new object[]{ new List<string> { "-2:", "sth" } },
                new object[]{ new List<string> { "0:", "not latin1:\u025b" } }
            };
        }

        [Fact]
        public void TestEncodeDecodeLongOptions()
        {
            var rand = new Random();
            int maxLen = 1000;
            int repeatCount = 10;
            for (int i = 0; i < repeatCount; i++)
            {
                string name = GenerateRandomString(rand, maxLen);
                string value = GenerateRandomString(rand, maxLen);
                int maxFragByteCount = rand.Next(maxLen) + 2; // ensure at least 2
                int spaceForFirst = rand.Next(maxFragByteCount);
                var values = ProtocolDatagramFragmenter.EncodeLongOption(name, value, maxFragByteCount, spaceForFirst);

                string[] actual = ProtocolDatagramFragmenter.DecodeLongOption(values);
                Assert.Equal(new string[] { name, value }, actual);
            }
        }

        private static string GenerateRandomString(Random rand, int maxLen)
        {
            int len = rand.Next(maxLen);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                // 50% prob of including non-ASCII char
                if (rand.Next(100) >= 50)
                {
                    sb.Append("\u025b");
                }
                else
                {
                    sb.Append("c");
                }
            }
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(CreateTestCreateFragmentsForAttributesData))]
        public void TestCreateFragmentsForAttributes(Dictionary<string, List<string>> attributes,
            int maxFragmentSize, int maxFragmentOptionsSize, List<string> optionsToSkip,
            List<ProtocolDatagram> expected)
        {
            var actual = ProtocolDatagramFragmenter.CreateFragmentsForAttributes(attributes,
                maxFragmentSize, maxFragmentOptionsSize, optionsToSkip);
            Assert.Equal(expected, actual, ProtocolDatagramComparer.Default);
        }

        public static List<object[]> CreateTestCreateFragmentsForAttributesData()
        {
            var testData = new List<object[]>();

            Dictionary<string, List<string>> attributes = null;
            int maxFragmentSize = 0;
            int maxFragmentOptionsSize = 0;
            List<string> optionsToSkip = null;
            var expected = new List<ProtocolDatagram>();
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>();
            maxFragmentSize = 0;
            maxFragmentOptionsSize = 0;
            optionsToSkip = new List<string>();
            expected = new List<ProtocolDatagram>();
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v1" } }
            };
            maxFragmentSize = 10;
            maxFragmentOptionsSize = 1000;
            optionsToSkip = new List<string>();
            var item1 = new ProtocolDatagram
            {
                ExpectedDatagramLength = 9,
                Options = new ProtocolDatagramOptions()
            };
            item1.Options.AllOptions.Add("k1", new List<string> { "v1" });
            expected = new List<ProtocolDatagram>
            {
                item1
            };
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v1", "v21" } }
            };
            maxFragmentSize = 10;
            maxFragmentOptionsSize = 1000;
            optionsToSkip = new List<string>();
            item1 = new ProtocolDatagram
            {
                ExpectedDatagramLength = 9,
                Options = new ProtocolDatagramOptions()
            };
            item1.Options.AllOptions.Add("k1", new List<string> { "v1" });
            var item2 = new ProtocolDatagram
            {
                ExpectedDatagramLength = 10,
                Options = new ProtocolDatagramOptions()
            };
            item2.Options.AllOptions.Add("k1", new List<string> { "v21" });
            expected = new List<ProtocolDatagram>
            {
                item1, item2
            };
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v1", "v21" } }
            };
            maxFragmentSize = 20;
            maxFragmentOptionsSize = 20;
            optionsToSkip = new List<string>();
            item1 = new ProtocolDatagram
            {
                ExpectedDatagramLength = 19,
                Options = new ProtocolDatagramOptions()
            };
            item1.Options.AllOptions.Add("k1", new List<string> { "v1", "v21" });
            expected = new List<ProtocolDatagram>
            {
                item1
            };
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v1", "v21" } },
                { "k2129709", new List<string>{ "seery", "himolim" } }
            };
            maxFragmentSize = 15;
            maxFragmentOptionsSize = 120;
            optionsToSkip = new List<string> { "k1" };
            expected = new List<ProtocolDatagram>();
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ "8:k21" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ "29709" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ "seery" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_1", new List<string>{ "8:k21" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_1", new List<string>{ "29709" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 15,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_1", new List<string>{ "himol" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 12,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_1", new List<string>{ "im" } }
            }));
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v10", "v11" } },
                { "k2", new List<string>{ "v21", "v23" } },
                { "k3", new List<string>{ "v31", "v34", "v35" } },
                { "k2129709", new List<string>{ "seery", "himolim" } }
            };
            maxFragmentSize = 30;
            maxFragmentOptionsSize = 120;
            // important to test that k2 is not skipped.
            optionsToSkip = new List<string> { "k2129709" };
            expected = new List<ProtocolDatagram>();
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 30,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v10", "v11" } },
                { "k2", new List<string>{ "v21"} }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 30,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "k2", new List<string>{ "v23" } },
                { "k3", new List<string>{ "v31", "v34" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 10,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "k3", new List<string>{ "v35" } }
            }));
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v10" } },
                { "k2129709", new List<string>{ "version-seery-x" } }
            };
            maxFragmentSize = 21;
            maxFragmentOptionsSize = 120;
            optionsToSkip = null;
            expected = new List<ProtocolDatagram>();
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 21,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v10" } },
                { "s_e_0", new List<string>{ "8" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 21,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ ":k2129709ve" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 21,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ "rsion-seery" } }
            }));
            expected.Add(AddOptions(new ProtocolDatagram
            {
                ExpectedDatagramLength = 12,
                Options = new ProtocolDatagramOptions()
            }, new Dictionary<string, List<string>>
            {
                { "s_e_0", new List<string>{ "-x" } }
            }));
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestCreateFragmentsForAttributesErrorData))]
        public void TestCreateFragmentsForAttributesError(Dictionary<string, List<string>> attributes,
            int maxFragmentSize, int maxFragmentOptionsSize, List<string> optionsToSkip,
            string expected)
        {
            var actual = Assert.ThrowsAny<Exception>(() => ProtocolDatagramFragmenter.CreateFragmentsForAttributes(attributes,
                maxFragmentSize, maxFragmentOptionsSize, optionsToSkip));
            if (expected != null)
            {
                Assert.Contains(expected, actual.Message);
            }
        }

        public static List<object[]> CreateTestCreateFragmentsForAttributesErrorData()
        {
            var testData = new List<object[]>();

            // only short options involved.
            Dictionary<string, List<string>> attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v1", "v2" } },
                { "k2", new List<string>{ "va", "vb" } },
            };
            int maxFragmentSize = 10;
            int maxFragmentOptionsSize = 20;
            List<string> optionsToSkip = null;
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, "adb4e1ef-c160-4e34-82ee-73f00699b4bd" });

            // long options involved.
            attributes = new Dictionary<string, List<string>>
            {
                { "k1", new List<string>{ "v110", "v210" } },
                { "k2", new List<string>{ "va", "vb" } },
            };
            maxFragmentSize = 11;
            maxFragmentOptionsSize = 20;
            optionsToSkip = new List<string> { "k2" };
            testData.Add(new object[] { attributes, maxFragmentSize, maxFragmentOptionsSize,
                optionsToSkip, "adb4e1ef-c160-4e34-82ee-73f00699b4bd" });

            return testData;
        }
    }
}
