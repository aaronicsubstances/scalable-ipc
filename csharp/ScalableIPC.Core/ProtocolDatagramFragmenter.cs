using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolDatagramFragmenter
    {
        public static readonly string EncodedOptionNamePrefix = ProtocolDatagramOptions.KnownOptionPrefix +
            "e_";

        private static readonly List<string> DefaultOptionsToSkip = new List<string>
        {
            ProtocolDatagramOptions.OptionNameIsLastInWindow, ProtocolDatagramOptions.OptionNameIsWindowFull,
            ProtocolDatagramOptions.OptionNameAbortCode, ProtocolDatagramOptions.OptionNameIsLastInWindowGroup,
            EncodedOptionNamePrefix
        };

        private static readonly int DefaultReservedSpace = ProtocolDatagram.MinDatagramSize
            + 50 // margin. must cover long versions of session id and window id, and
                 // also the last in window and last in window group options.
            ;

        private readonly ProtocolMessage _message;
        private readonly int _maxFragmentSize;
        private readonly int _maxFragmentBatchSize;
        private readonly int _maxFragmentOptionsSize;
        private readonly List<string> _optionsToSkip;
        private int _usedDataLength;
        private List<ProtocolDatagram> _optionsTemplate;
        private bool _done;

        public ProtocolDatagramFragmenter(ProtocolMessage message, int maxFragmentSize, List<string> extraOptionsToSkip)
            :this(message, maxFragmentSize, extraOptionsToSkip, DefaultReservedSpace, DefaultOptionsToSkip,
                 ProtocolDatagram.MaxOptionByteCount, ProtocolDatagram.MaxDatagramSize)
        {

        }

        // helps with testing
        internal ProtocolDatagramFragmenter(ProtocolMessage message, int maxFragmentSize, List<string> extraOptionsToSkip,
            int defaultReservedSpace, List<string> defaultOptionsToSkip, int maxFragmentOptionsSize, int maxFragmentBatchSize)
        {
            _message = message;
            _maxFragmentSize = maxFragmentSize - defaultReservedSpace;
            _optionsToSkip = new List<string>(defaultOptionsToSkip);
            if (extraOptionsToSkip != null)
            {
                _optionsToSkip.AddRange(extraOptionsToSkip);
            }
            _maxFragmentBatchSize = maxFragmentBatchSize;
            _maxFragmentOptionsSize = maxFragmentOptionsSize;
            _usedDataLength = 0;
            _done = false;
        }

        public List<ProtocolDatagram> Next()
        {
            if (_done)
            {
                return new List<ProtocolDatagram>();
            }

            if (_optionsTemplate == null)
            {
                _optionsTemplate = CreateFragmentsForAttributes(_message.Attributes,
                    _maxFragmentSize, _maxFragmentOptionsSize,
                    _optionsToSkip);
            }

            var nextFragments = DuplicateFragmentsFromOptions();
            int bytesNeeded = nextFragments.Sum(x => x.ExpectedDatagramLength);

            // use to detect infinite looping resulting from lack of progress.
            int prevUsedDataLength = _usedDataLength;

            // first phase: top up.
            foreach (var nextFragment in nextFragments)
            {
                if (bytesNeeded >= _maxFragmentBatchSize)
                {
                    break;
                }
                int extraSpace = _message.DataLength - _usedDataLength;
                if (extraSpace > _maxFragmentBatchSize - bytesNeeded)
                {
                    extraSpace = _maxFragmentBatchSize - bytesNeeded;
                }
                if (extraSpace > _maxFragmentSize - nextFragment.ExpectedDatagramLength)
                {
                    extraSpace = _maxFragmentSize - nextFragment.ExpectedDatagramLength;
                }
                if (extraSpace <= 0)
                {
                    continue;
                }
                bytesNeeded += extraSpace;
                nextFragment.DataLength = extraSpace;
                nextFragment.DataBytes = _message.DataBytes;
                nextFragment.DataOffset = _message.DataOffset + _usedDataLength;
                _usedDataLength += extraSpace;

                if (_usedDataLength == _message.DataLength)
                {
                    _done = true;
                    break;
                }
            }

            // next phase: add new fragments till window is filled up.
            while (!_done && bytesNeeded < _maxFragmentBatchSize)
            {
                int spaceForData = _message.DataLength - _usedDataLength;
                if (spaceForData > _maxFragmentBatchSize - bytesNeeded)
                {
                    spaceForData = _maxFragmentBatchSize - bytesNeeded;
                }
                if (spaceForData > _maxFragmentSize)
                {
                    spaceForData = _maxFragmentSize;
                }
                if (spaceForData <= 0)
                {
                    break;
                }
                bytesNeeded += spaceForData;
                var nextFragment = new ProtocolDatagram();
                nextFragments.Add(nextFragment);
                nextFragment.DataLength = spaceForData;
                nextFragment.DataBytes = _message.DataBytes;
                nextFragment.DataOffset = _message.DataOffset + _usedDataLength;
                _usedDataLength += spaceForData;

                if (_usedDataLength == _message.DataLength)
                {
                    _done = true;
                }
            }

            // for case of empty input, create a corresponding empty datagram.
            if (nextFragments.Count == 0 && _message.DataLength == 0 && _optionsTemplate.Count == 0)
            {
                nextFragments.Add(new ProtocolDatagram());
                _done = true;
            }

            // Ensure progress has been made.
            if (nextFragments.Count == 0 || (!_done && _usedDataLength <= prevUsedDataLength))
            {
                throw new Exception("Wrong algorithm. Infinite loop detected as no data was added to current iteration for fragments");
            }

            // clear out to eliminate false expectations
            foreach (var nextFragment in nextFragments)
            {
                nextFragment.ExpectedDatagramLength = 0;
            }

            return nextFragments;
        }

        private List<ProtocolDatagram> DuplicateFragmentsFromOptions()
        {
            var duplicates = new List<ProtocolDatagram>();
            foreach (var bareDatagram in _optionsTemplate)
            {
                var duplicate = new ProtocolDatagram
                {
                    ExpectedDatagramLength = bareDatagram.ExpectedDatagramLength,
                    Options = new ProtocolDatagramOptions()
                };
                foreach (var pair in bareDatagram.Options.AllOptions)
                {
                    duplicate.Options.AllOptions.Add(pair.Key, new List<string>(pair.Value));
                }
            }
            return duplicates;
        }

        public static List<ProtocolDatagram> CreateFragmentsForAttributes(Dictionary<string, List<string>> attributes,
            int maxFragmentSize, int maxFragmentOptionsSize, List<string> optionsToSkip)
        {
            var fragments = new List<ProtocolDatagram>();
            if (attributes == null)
            {
                return fragments;
            }
            int encodedOptionCount = 0;
            ProtocolDatagramOptions latest = new ProtocolDatagramOptions();
            int latestSize = 0;
            int totalSize = 0;
            foreach (var kvp in attributes)
            {
                // treat options to skip as prefix filters
                if (optionsToSkip.Any(s => s.StartsWith(kvp.Key)))
                {
                    continue;
                }
                int keyBytes = ProtocolDatagram.CountBytesInString(kvp.Key);
                foreach (var attVal in kvp.Value)
                {
                    var minBytesNeeded = keyBytes + ProtocolDatagram.CountBytesInString(attVal)
                        + 2 // for option value length indicator 
                        + 1 // null terminator
                        ;
                    string optionName = kvp.Key;
                    List<string> optionValues;
                    int optionNameBytes;
                    if (minBytesNeeded <= maxFragmentSize)
                    {
                        optionName = kvp.Key;
                        optionNameBytes = keyBytes;
                        optionValues = new List<string> { attVal };
                    }
                    else
                    {
                        if (minBytesNeeded > maxFragmentOptionsSize)
                        {
                            throw new Exception($"Attributes too large for max option bytes: {minBytesNeeded} > {maxFragmentOptionsSize}");
                        }
                        // encode long option
                        optionName = EncodedOptionNamePrefix + encodedOptionCount;
                        optionNameBytes = ProtocolDatagram.CountBytesInString(optionName);
                        optionValues = EncodeLongOption(kvp.Key, attVal, maxFragmentSize);
                        encodedOptionCount++;
                    }
                    foreach (var optionValue in optionValues)
                    {
                        var optionBytesNeeded = optionNameBytes + ProtocolDatagram.CountBytesInString(optionValue)
                           + 2 // for option value length indicator 
                           + 1 // null terminator
                           ;
                        totalSize += optionBytesNeeded;
                        if (totalSize > maxFragmentOptionsSize)
                        {
                            throw new Exception($"Attributes too large for max option bytes: {totalSize} > {maxFragmentOptionsSize}");
                        }
                        if (latestSize + optionBytesNeeded > maxFragmentSize)
                        {
                            var fragment = new ProtocolDatagram
                            {
                                ExpectedDatagramLength = latestSize,
                                Options = latest
                            };
                            fragments.Add(fragment);
                            latest = new ProtocolDatagramOptions();
                            latestSize = 0;
                        }
                        latest.AddOption(optionName, optionValue);
                        latestSize += optionBytesNeeded;
                    }
                }
            }
            if (latestSize > 0)
            {
                var lastFragment = new ProtocolDatagram
                {
                    ExpectedDatagramLength = latestSize,
                    Options = latest
                };
                fragments.Add(lastFragment);
            }
            return fragments;
        }

        public static List<string> EncodeLongOption(string name, string value, int maxFragByteCount)
        {
            // encoding task is greatly simplified when number of bytes per char is easily determined.
            // hence convert to Latin 1 (where subset of ASCII chars correspond to a byte, and the rest 
            // consume 2 bytes) before splitting.
            var optionWithLengthPrefix = name.Length + ":" + name + value;
            var bytes = ProtocolDatagram.ConvertStringToBytes(optionWithLengthPrefix);
            var latin1Encoded = ProtocolDatagram.ConvertBytesToLatin1(bytes, 0, bytes.Length);
            var fragments = new List<string>();
            int lastFragIndex = 0, nextFragByteCount = 0;
            for (int i = 0; i < latin1Encoded.Length; i++)
            {
                int chByteCount = 1;
                if (latin1Encoded[i] > 0x7f)
                {
                    chByteCount = 2; // utf8 encodes 0x80-0xff with 2 bytes
                }
                if (chByteCount > maxFragByteCount)
                {
                    throw new Exception("Cannot encode long option with insufficient max fragment size: " + maxFragByteCount);
                }
                if (nextFragByteCount + chByteCount > maxFragByteCount)
                {
                    fragments.Add(latin1Encoded.Substring(lastFragIndex, i - lastFragIndex));
                    lastFragIndex = i;
                    nextFragByteCount = 0;
                }
                nextFragByteCount += chByteCount;
            }
            // add remainder if any
            if (lastFragIndex < latin1Encoded.Length)
            {
                fragments.Add(latin1Encoded.Substring(lastFragIndex));
            }
            return fragments;
        }

        public static string[] DecodeLongOption(List<string> values)
        {
            var latin1Encoded = string.Join("", values);
            // validate that string contains only valid latin1 chars.
            foreach (var c in latin1Encoded)
            {
                if (c > 0xff)
                {
                    throw new Exception("Invalid encoded long option");
                }
            }
            var bytes = ProtocolDatagram.ConvertLatin1ToBytes(latin1Encoded);
            var originalOptionWithLengthPrefix = ProtocolDatagram.ConvertBytesToString(bytes, 0, bytes.Length);
            var lengthDelimIdx = originalOptionWithLengthPrefix.IndexOf(":");
            if (lengthDelimIdx == -1)
            {
                throw new Exception("Invalid encoded long option");
            }
            var nameLengthStr = originalOptionWithLengthPrefix.Substring(0, lengthDelimIdx);
            int nameLength;
            if (!int.TryParse(nameLengthStr, out nameLength))
            {
                throw new Exception("Invalid encoded long option");
            }
            if (nameLength < 0 || nameLength > originalOptionWithLengthPrefix.Length)
            {
                throw new Exception("Invalid encoded long option");
            }
            var originalName = originalOptionWithLengthPrefix.Substring(lengthDelimIdx + 1, nameLength);
            var originalValue = originalOptionWithLengthPrefix.Substring(lengthDelimIdx + 1 + nameLength);
            return new string[] { originalName, originalValue };
        }
    }
}
