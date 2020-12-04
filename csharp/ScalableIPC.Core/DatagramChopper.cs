using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core
{
    public class DatagramChopper
    {
        private static readonly List<string> StandardOptionsToSkip = new List<string>
        {
            ProtocolDatagramOptions.OptionNameIsLastInWindow, ProtocolDatagramOptions.OptionNameIsWindowFull,
            ProtocolDatagramOptions.OptionNameAbortCode, ProtocolDatagramOptions.OptionNameIsLastInWindowGroup
        };

        private readonly byte[] _data;
        private readonly int _dataOffset;
        private readonly int _dataLength;
        private readonly List<string[]> _options;
        private readonly List<string> _extraOptionsToSkip;
        private int _usedOptionCount;
        private int _usedDataByteCount;
        private bool _started;
        private ProtocolDatagram _nextPdu;
        private bool _nextPduReturned;

        public DatagramChopper(ProtocolDatagram fullMessage, int maxPduSize, List<string> extraOptionsToSkip)
        {
            if (fullMessage.DataBytes != null)
            {
                _data = fullMessage.DataBytes;
                _dataOffset = fullMessage.DataOffset;
                _dataLength = fullMessage.DataLength;
            }
            else
            {
                _data = new byte[0];
                _dataOffset = 0;
                _dataLength = 0;
            }
            if (fullMessage.Options != null)
            {
                _options = fullMessage.Options.GenerateList().ToList();
            }
            else
            {
                _options = new List<string[]>();
            }

            MaxPduSize = maxPduSize;

            if (extraOptionsToSkip != null)
            {
                _extraOptionsToSkip = extraOptionsToSkip;
            }
            else
            {
                _extraOptionsToSkip = new List<string>();
            }

            _usedOptionCount = 0;
            _usedDataByteCount = 0;
            _started = false;
            _nextPdu = null;
            _nextPduReturned = false;
        }

        public int MaxPduSize { get; }

        public bool HasNext(int reserveSpaceByteCount)
        {
            if (_started && !_nextPduReturned)
            {
                return _nextPdu != null;
            }
            _nextPdu = null;
            _nextPduReturned = false;

            // send all options first, then data.

            reserveSpaceByteCount += ProtocolDatagram.MinDatagramSize;

            var subOptions = new ProtocolDatagramOptions();
            int spaceUsed = 0;
            while (_usedOptionCount < _options.Count)
            {
                string[] pair = _options[_usedOptionCount];
                string k = pair[0];

                if (StandardOptionsToSkip.Contains(k) && !_extraOptionsToSkip.Contains(k))
                {
                    // skip standard session layer options and user-defined extra ones
                    // which could interfere with bulk sending.
                }
                else
                {
                    int kLength = ProtocolDatagram.CountBytesInString(k);
                    var v = pair[1];
                    int vLength = ProtocolDatagram.CountBytesInString(v);
                    int extraSpaceNeeded = kLength + vLength + 2; // 2 for null terminator count.

                    if (spaceUsed + extraSpaceNeeded > MaxPduSize - reserveSpaceByteCount)
                    {
                        break;
                    }
                    subOptions.AddOption(k, v, false);
                    spaceUsed += extraSpaceNeeded;
                }

                _usedOptionCount++;
            }

            var doneWithOptions = _usedOptionCount >= _options.Count;

            int dataChunkOffset = _usedDataByteCount;
            int dataChunkLength = 0;
            if (doneWithOptions)
            {
                dataChunkLength = Math.Max(0,
                    Math.Min(MaxPduSize - reserveSpaceByteCount - spaceUsed, _dataLength - dataChunkOffset));
                _usedDataByteCount += dataChunkLength;
                spaceUsed += dataChunkLength;
            }

            // detect endless looping.
            if (spaceUsed == 0)
            {
                if (!doneWithOptions)
                {
                    throw new Exception("Cannot make further progress... Option at index" +
                        $"{_usedOptionCount} is too long to fit into pdu.");
                }
                else if (_usedDataByteCount < _dataLength && dataChunkLength == 0)
                {
                    throw new Exception("Cannot make further progress... Maximum PDU size of " +
                        $"{MaxPduSize} is too small to accomodate data payload");
                }
            }

            // return null to indicate end of iteration, except if we have not
            // started, in which case something (possibly empty) has to be returned.
            if (spaceUsed > 0)
            {
                _nextPdu = new ProtocolDatagram
                {
                    DataBytes = _data,
                    DataOffset = _dataOffset + dataChunkOffset,
                    DataLength = dataChunkLength,
                    Options = subOptions
                };
            }
            else
            {
                if (!_started)
                {
                    _nextPdu = new ProtocolDatagram();
                }
            }

            _started = true;

            return _nextPdu != null;
        }

        public ProtocolDatagram Next()
        {
            _nextPduReturned = true; 
            return _nextPdu;
        }
    }
}
