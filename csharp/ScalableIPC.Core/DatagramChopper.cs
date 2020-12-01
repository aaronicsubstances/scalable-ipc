using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core
{
    public class DatagramChopper
    {
        private static readonly List<string> OptionsToSkipElseCouldInterfere = new List<string>
        {
            ProtocolDatagramOptions.OptionNameIsLastInWindow, ProtocolDatagramOptions.OptionNameIsWindowFull
        };

        private readonly byte[] _data;
        private readonly List<string[]> _options;
        private readonly int _maxPduSize;
        private int _usedOptionCount;
        private int _usedDataByteCount;
        private bool _doneWithOptions;
        private bool _started;

        public DatagramChopper(byte[] data, ProtocolDatagramOptions options, int maxPduSize)
        {
            _data = data;
            _options = options.GenerateList().ToList();
            _maxPduSize = maxPduSize;

            _usedOptionCount = 0;
            _usedDataByteCount = 0;
            _doneWithOptions = false;
            _started = false;
        }

        public ProtocolDatagram Next(int reserveSpaceByteCount, bool peekOnly)
        {
            // send all options first, then data.

            var savedState = new
            {
                Started = _started,
                DoneWithOptions= _doneWithOptions,
                UsedOptionCount = _usedOptionCount,
                UsedDataByteCount = _usedDataByteCount
            };

            reserveSpaceByteCount += ProtocolDatagram.MinDatagramSize;

            var subOptions = new ProtocolDatagramOptions();
            int spaceUsed = 0;
            if (!_doneWithOptions)
            {
                bool nextOptionsSpaceUsedUp = false;
                while (_usedOptionCount < _options.Count)
                {
                    string[] pair = _options[_usedOptionCount];
                    string k = pair[0];

                    // skip known session layer options which could interfere with bulk sending.
                    if (!OptionsToSkipElseCouldInterfere.Contains(k))
                    {
                        int kLength = ProtocolDatagram.ConvertStringToBytes(k).Length;
                        var v = pair[1];
                        int vLength = ProtocolDatagram.ConvertStringToBytes(v).Length;
                        int extraSpaceNeeded = kLength + vLength + 2; // 2 for null terminator count.

                        if (spaceUsed + extraSpaceNeeded > _maxPduSize - reserveSpaceByteCount)
                        {
                            nextOptionsSpaceUsedUp = true;
                            break;
                        }
                        subOptions.AddOption(k, v);
                        spaceUsed += extraSpaceNeeded;
                    }

                    _usedOptionCount++;
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

            // return null to indicate end of iteration, except if we have not
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
                _usedOptionCount = savedState.UsedOptionCount;
                _usedDataByteCount = savedState.UsedDataByteCount;
                _doneWithOptions = savedState.DoneWithOptions;
                _started = savedState.Started;
            }

            return nextPdu;
        }
    }
}
