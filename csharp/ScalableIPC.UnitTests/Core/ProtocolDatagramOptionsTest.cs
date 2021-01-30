using ScalableIPC.Core;
using ScalableIPC.UnitTests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace ScalableIPC.UnitTests.Core
{
    public class ProtocolDatagramOptionsTest
    {
        [Theory]
        [MemberData(nameof(CreateTestEqualsData))]
        public void TestEquals(ProtocolDatagramOptions x, ProtocolDatagramOptions y, bool shouldBeEqual)
        {
            Assert.Equal(x, y, shouldBeEqual ? ProtocolDatagramOptionsComparer.Default :
                new ProtocolDatagramOptionsComparer(true));
        }

        public static List<object[]> CreateTestEqualsData()
        {
            var testData = new List<object[]>();

            testData.Add(
                new object[] { new ProtocolDatagramOptions(), new ProtocolDatagramOptions(), true });

            testData.Add(
                new object[] { new ProtocolDatagramOptions(), new ProtocolDatagramOptions { IdleTimeout = 3 }, false });

            var firstInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = ""
            };
            var secondInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = ""
            };
            testData.Add(new object[] { firstInstance, secondInstance, true });

            firstInstance = new ProtocolDatagramOptions();
            firstInstance.AllOptions.Add("k1", new List<string> { "v1" });
            firstInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            secondInstance = new ProtocolDatagramOptions();
            secondInstance.AllOptions.Add("k1", new List<string>());
            testData.Add(new object[] { firstInstance, secondInstance, false });

            firstInstance = new ProtocolDatagramOptions();
            firstInstance.AllOptions.Add("k1", new List<string> { "v1" });
            firstInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            secondInstance = new ProtocolDatagramOptions();
            secondInstance.AllOptions.Add("k1", new List<string> { "v1" });
            secondInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            testData.Add(new object[] { firstInstance, secondInstance, true });

            // test that order of key insertion is respected.
            firstInstance = new ProtocolDatagramOptions();
            firstInstance.AllOptions.Add("k1", new List<string> { "v1" });
            firstInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            secondInstance = new ProtocolDatagramOptions();
            secondInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            secondInstance.AllOptions.Add("k1", new List<string> { "v1" });
            testData.Add(new object[] { firstInstance, secondInstance, false });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestAddOptionData))]
        public void TestAddOption(List<string[]> options, ProtocolDatagramOptions expected)
        {
            var actual = new ProtocolDatagramOptions();
            foreach (var option in options)
            {
                actual.AddOption(option[0], option[1]);
            }
            Assert.Equal(expected, actual, ProtocolDatagramOptionsComparer.Default);
        }

        public static List<object[]> CreateTestAddOptionData()
        {
            var testData = new List<object[]>();

            var testOptions = new List<string[]>();
            var expected = new ProtocolDatagramOptions();
            testData.Add(new object[] { testOptions, expected });

            testOptions = new List<string[]>
            {
                new string[]{ "x", "1" }
            };
            expected = new ProtocolDatagramOptions();
            expected.AllOptions.Add("x", new List<string> { "1" });
            testData.Add(new object[] { testOptions, expected });

            testOptions = new List<string[]>
            {
                new string[]{ "x", "1" },
                new string[]{ "y", "2" },
                new string[]{ "x", "2" }
            };
            expected = new ProtocolDatagramOptions();
            expected.AllOptions.Add("x", new List<string> { "1", "2" });
            expected.AllOptions.Add("y", new List<string> { "2" });
            testData.Add(new object[] { testOptions, expected });

            testOptions = new List<string[]>
            {
                new string[]{ "y", "1" },
                new string[]{ "y", "2" },
                new string[]{ "y", "2" }
            };
            expected = new ProtocolDatagramOptions();
            expected.AllOptions.Add("y", new List<string> { "1", "2", "2" });
            testData.Add(new object[] { testOptions, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestParseKnownOptionsData))]
        public void TestParseKnownOptions(ProtocolDatagramOptions input, ProtocolDatagramOptions expected)
        {
            input.ParseKnownOptions();
            Assert.Equal(expected, input, ProtocolDatagramOptionsComparer.Default);
        }

        public static List<object[]> CreateTestParseKnownOptionsData()
        {
            var testData = new List<object[]>();

            // test that case of booleans doesn't matter
            var inputInstance = new ProtocolDatagramOptions();
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "3" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "-1", "4" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "TRUE" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "FALSE" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameTraceId, new List<string> { "a", "b", "" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string>());
            var expectedInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = ""
            };
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "3" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "-1", "4" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "TRUE" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "FALSE" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameTraceId, new List<string> { "a", "b", "" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string>());
            testData.Add(new object[] { inputInstance, expectedInstance });

            // test that known options are reset before parsing
            inputInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = true
            };
            inputInstance.AllOptions.Add("k1", new List<string> { "v1" });
            inputInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            expectedInstance = new ProtocolDatagramOptions();
            expectedInstance.AllOptions.Add("k1", new List<string> { "v1" });
            expectedInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            testData.Add(new object[] { inputInstance, expectedInstance });

            // test that old value is overwritten by parse
            inputInstance = new ProtocolDatagramOptions
            {
                IsLastInWindowGroup = true,
                IsWindowFull = false,
                MaxWindowSize = 8
            };
            inputInstance.AllOptions.Add("k1", new List<string> { "v1" });
            inputInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true" });
            inputInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameMaxWindowSize, new List<string> { "9" });
            expectedInstance = new ProtocolDatagramOptions()
            {
                IsWindowFull = true,
                MaxWindowSize = 9
            };
            expectedInstance.AllOptions.Add("k1", new List<string> { "v1" });
            expectedInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true" });
            expectedInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameMaxWindowSize, new List<string> { "9" });
            testData.Add(new object[] { inputInstance, expectedInstance });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestParseKnownOptionsWithErrorData))]
        public void TestParseKnownOptionsWithError(ProtocolDatagramOptions input)
        {
            Assert.ThrowsAny<Exception>(() => input.ParseKnownOptions());
        }

        public static List<object[]> CreateTestParseKnownOptionsWithErrorData()
        {
            var testData = new List<object[]>();

            var instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add("k1", new List<string> { "v1" });
            instance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "0" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "a" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "q" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "" });
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "10" });
            testData.Add(new object[] { instance });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestGenerateListData))]
        public void TestGenerateList(ProtocolDatagramOptions instance, List<string[]> expected)
        {
            var actual = instance.GenerateList().ToList();
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestGenerateListData()
        {
            var testData = new List<object[]>();

            testData.Add(new object[] { new ProtocolDatagramOptions(), new List<string[]>() });

            var instance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = false
            };
            var expected = new List<string[]>
            {
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "4" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIdleTimeout, "3" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "True" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameTraceId, "" } },
            };
            testData.Add(new object[] { instance, expected });

            instance = new ProtocolDatagramOptions();
            instance.AllOptions.Add("k1", new List<string> { "v1" });
            instance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            expected = new List<string[]>
            {
                { new string[]{ "k1", "v1" } },
                { new string[]{ "k2", "v2a" } },
                { new string[]{ "k2", "v2b" } },
            };
            testData.Add(new object[] { instance, expected });

            // test that options are added if absent in AllOptions.
            instance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "3f61a6c3-4736-4b6d-bbc1-416ad9b30493",
                IsWindowFull = false
            };
            instance.AllOptions.Add("k1", new List<string> { "v1" });
            instance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            expected = new List<string[]>
            {
                { new string[]{ "k1", "v1" } },
                { new string[]{ "k2", "v2a" } },
                { new string[]{ "k2", "v2b" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "4" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIdleTimeout, "3" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "True" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameTraceId, "3f61a6c3-4736-4b6d-bbc1-416ad9b30493" } },
            };
            testData.Add(new object[] { instance, expected });

            // test that options are NOT added if present in AllOptions with same last value. include different letter case for booleans.
            instance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "test",
                IsWindowFull = false
            };
            instance.AllOptions.Add("k1", new List<string> { "v1" });
            instance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "0", "4" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "3" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "true" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "FALSE" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true", "False" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameTraceId, new List<string> { "test" });
            expected = new List<string[]>
            {
                { new string[]{ "k1", "v1" } },
                { new string[]{ "k2", "v2a" } },
                { new string[]{ "k2", "v2b" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "0" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "4" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIdleTimeout, "3" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "true" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, "FALSE" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "true" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameTraceId, "test" } },
            };
            testData.Add(new object[] { instance, expected });

            // test that options are added if present in AllOptions with different last value.
            instance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "b1ea7f1b-9862-42b6-a0c5-c751f35b474f",
                IsWindowFull = false
            };
            instance.AllOptions.Add("k1", new List<string> { "v1" });
            instance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "0" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "30" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "true", "1" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "0" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true" });
            instance.AllOptions.Add(ProtocolDatagramOptions.OptionNameTraceId, new List<string> { "test" });
            expected = new List<string[]>
            {
                { new string[]{ "k1", "v1" } },
                { new string[]{ "k2", "v2a" } },
                { new string[]{ "k2", "v2b" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "0" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameAbortCode, "4" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIdleTimeout, "30" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIdleTimeout, "3" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "true" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "1" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindow, "True" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, "0" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "true" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameIsWindowFull, "False" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameTraceId, "test" } },
                { new string[]{ ProtocolDatagramOptions.OptionNameTraceId, "b1ea7f1b-9862-42b6-a0c5-c751f35b474f" } }
            };
            testData.Add(new object[] { instance, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestTransferParsedKnownOptionsToData))]
        public void TestTransferKnownOptionsTo(ProtocolDatagramOptions src, ProtocolDatagramOptions dest,
            ProtocolDatagramOptions expected)
        {
            src.TransferParsedKnownOptionsTo(dest);
            Assert.Equal(expected, dest, ProtocolDatagramOptionsComparer.Default);
        }

        public static List<object[]> CreateTestTransferParsedKnownOptionsToData()
        {
            var testData = new List<object[]>();

            testData.Add(new object[] { new ProtocolDatagramOptions(), new ProtocolDatagramOptions(), new ProtocolDatagramOptions() });

            // test that nulls are NOT transferred
            var srcInstance = new ProtocolDatagramOptions();
            var destInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = false,
                MaxWindowSize = 10
            };
            var expected = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = false,
                MaxWindowSize = 10
            };
            testData.Add(new object[] { srcInstance, destInstance, expected });

            // test all known options are transferred.
            srcInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = true,
                TraceId = "t",
                IsWindowFull = true
            };
            destInstance = new ProtocolDatagramOptions();
            expected = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = true,
                TraceId = "t",
                IsWindowFull = true
            };
            testData.Add(new object[] { srcInstance, destInstance, expected });

            // test that all old values are overwitten, even if new ones are "falsy".
            srcInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 0,
                AbortCode = 0,
                IsLastInWindow = false,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = false
            };
            destInstance = new ProtocolDatagramOptions
            {
                IdleTimeout = 3,
                AbortCode = 4,
                IsLastInWindow = true,
                IsLastInWindowGroup = true,
                TraceId = "t",
                IsWindowFull = true
            };
            expected = new ProtocolDatagramOptions
            {
                IdleTimeout = 0,
                AbortCode = 0,
                IsLastInWindow = false,
                IsLastInWindowGroup = false,
                TraceId = "",
                IsWindowFull = false
            };
            testData.Add(new object[] { srcInstance, destInstance, expected });

            // test that AllOptions aren't transferred at all.
            srcInstance = new ProtocolDatagramOptions
            {
                IsLastInWindowGroup = true,
                IsWindowFull = false
            };
            srcInstance.AllOptions.Add("k1", new List<string> { "v1" });
            srcInstance.AllOptions.Add("k2", new List<string> { "v2a", "v2b" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout, new List<string> { "3" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameAbortCode, new List<string> { "-1", "4" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow, new List<string> { "true" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup, new List<string> { "false" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameTraceId, new List<string> { "a", "b", "" });
            srcInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "false" });
            destInstance = new ProtocolDatagramOptions()
            {
                IdleTimeout = 90,
                IsWindowFull = true
            };
            destInstance.AllOptions.Add("k1", new List<string>());
            destInstance.AllOptions.Add("k3", new List<string> { "v3" });
            destInstance.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true" });
            expected = new ProtocolDatagramOptions()
            {
                IdleTimeout = 90,
                IsWindowFull = false,
                IsLastInWindowGroup = true,
            };
            expected.AllOptions.Add("k1", new List<string>());
            expected.AllOptions.Add("k3", new List<string> { "v3" });
            expected.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsWindowFull, new List<string> { "true" });
            testData.Add(new object[] { srcInstance, destInstance, expected });

            return testData;
        }
    }
}
