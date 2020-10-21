using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class DatagramChopper
    {
        private readonly byte[] _data;
        private readonly List<string> _optionKeys;
        private readonly Dictionary<string, List<string>> _options;
        private readonly int _maxPduSize;

        private int _usedOptionKeyCount;
        private int _usedOptionValueCount;
        private int _usedDataByteCount;
        private bool _doneWithOptions;
        private bool _started;

        public DatagramChopper(byte[] data, Dictionary<string, List<string>> options, int maxPduSize)
        {
            _data = data;
            _optionKeys = options.Keys.ToList();
            _options = options;
            _maxPduSize = maxPduSize;

            _started = false;
        }

        public ProtocolDatagram Next(int reserveSpaceByteCount, bool peekOnly)
        {
            // send all options first, then data.

            var savedState = new
            {
                Started = _started,
                DoneWithOptions= _doneWithOptions,
                UsedOptionKeyCount = _usedOptionKeyCount, 
                UsedOptionValueCount = _usedOptionValueCount,
                UsedDataByteCount = _usedDataByteCount
            };

            if (!_started)
            {
                _usedOptionKeyCount = 0;
                _usedOptionValueCount = 0;
                _doneWithOptions = false;

                // locate first key with non-empty values.
                foreach (var k in _optionKeys)
                {
                    if (_options[k].Count > 0)
                    {
                        break;
                    }
                    _usedOptionKeyCount++;
                }
                _doneWithOptions = _usedOptionKeyCount >= _optionKeys.Count;
            }

            reserveSpaceByteCount += ProtocolDatagram.MinDatagramSize;
            const int nullByteCountNeededPerOption = 2;

            var subOptions = new Dictionary<string, List<string>>();
            int spaceUsed = 0;
            if (!_doneWithOptions)
            {
                bool nextOptionsSpaceUsedUp = false;
                while (_usedOptionKeyCount < _optionKeys.Count)
                {
                    string k = _optionKeys[_usedOptionKeyCount];
                    var optionValues = _options[k];
                    int kLength = ProtocolDatagram.ConvertStringToBytes(k).Length;
                    while (_usedOptionValueCount < optionValues.Count)
                    {
                        var v = optionValues[_usedOptionValueCount];
                        int vLength = ProtocolDatagram.ConvertStringToBytes(v).Length;
                        int extraSpaceNeeded = kLength + vLength + nullByteCountNeededPerOption;

                        if (spaceUsed + extraSpaceNeeded > _maxPduSize - reserveSpaceByteCount)
                        {
                            nextOptionsSpaceUsedUp = true;
                            break;
                        }
                        List<string> subOptionList;
                        if (subOptions.ContainsKey(k))
                        {
                            subOptionList = subOptions[k];
                        }
                        else
                        {
                            subOptionList = new List<string>();
                            subOptions.Add(k, subOptionList);
                        }
                        subOptionList.Add(v);
                        _usedOptionValueCount++;
                        spaceUsed += extraSpaceNeeded;
                    }

                    if (nextOptionsSpaceUsedUp)
                    {
                        break;
                    }

                    _usedOptionKeyCount++;
                    _usedOptionValueCount = 0;
                }

                if (!nextOptionsSpaceUsedUp)
                {
                    _doneWithOptions = true;
                }
            }

            int dataChunkOffset = _usedDataByteCount;
            int dataChunkLength = 0;
            if (_doneWithOptions)
            {
                dataChunkLength = Math.Max(0, 
                    Math.Min(_maxPduSize - spaceUsed - reserveSpaceByteCount, _data.Length - dataChunkOffset));
                _usedDataByteCount += dataChunkLength;
                spaceUsed += dataChunkLength;
            }

            // detect endless looping.
            if (spaceUsed == 0)
            {
                if (!_doneWithOptions || (_usedDataByteCount < _data.Length && dataChunkLength == 0))
                {
                    throw new Exception("Endless looping detected.");
                }
            }

            // return null to indicate end of iteration, except if we are not
            // started, in which case something (possibly empty) has to be returned.
            ProtocolDatagram nextPdu = null;
            if (spaceUsed > 0)
            {
                nextPdu = new ProtocolDatagram
                {
                    DataBytes = _data,
                    DataOffset = dataChunkOffset,
                    DataLength = dataChunkLength,
                    Options = subOptions
                };
            }
            else
            {
                if (!_started)
                {
                    nextPdu = new ProtocolDatagram();
                }
            }

            _started = false;

            if (peekOnly)
            {
                _usedOptionKeyCount = savedState.UsedOptionKeyCount;
                _usedOptionValueCount = savedState.UsedOptionValueCount;
                _usedDataByteCount = savedState.UsedDataByteCount;
                _doneWithOptions = savedState.DoneWithOptions;
                _started = savedState.Started;
            }

            return nextPdu;
        }
    }
}
