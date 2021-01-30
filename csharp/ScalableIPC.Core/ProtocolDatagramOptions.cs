using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolDatagramOptions
    {
        // Reserve s_ prefix for known options.
        public const string KnownOptionPrefix = "s_";

        public const string OptionNameIdleTimeout = KnownOptionPrefix + "idle_timeout";
        public const string OptionNameAbortCode = KnownOptionPrefix + "abort_code";
        public const string OptionNameIsWindowFull = KnownOptionPrefix + "10";
        public const string OptionNameMaxWindowSize = KnownOptionPrefix + "20";
        public const string OptionNameIsLastInWindow = KnownOptionPrefix + "01";
        public const string OptionNameIsLastInWindowGroup = KnownOptionPrefix + "02";
        public const string OptionNameTraceId = KnownOptionPrefix + "traceId";

        public ProtocolDatagramOptions()
        {
            // use of Dictionary is part of reference implementation API to ensure that key insertion order 
            // is the same as key retrieval order.
            AllOptions = new Dictionary<string, List<string>>();
        }

        public Dictionary<string, List<string>> AllOptions { get; }

        // Known options.
        public int? IdleTimeout { get; set; }
        public int? AbortCode { get; set; }
        public bool? IsWindowFull { get; set; }
        public bool? IsLastInWindow { get; set; }
        public bool? IsLastInWindowGroup { get; set; }
        public string TraceId { get; set; }
        public int? MaxWindowSize { get; set; }
        
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(ProtocolDatagramOptions)).Append("{");
            sb.Append(nameof(IdleTimeout)).Append("=").Append(IdleTimeout);
            sb.Append(", ");
            sb.Append(nameof(AbortCode)).Append("=").Append(AbortCode);
            sb.Append(", ");
            sb.Append(nameof(IsWindowFull)).Append("=").Append(IsWindowFull);
            sb.Append(", ");
            sb.Append(nameof(IsLastInWindow)).Append("=").Append(IsLastInWindow);
            sb.Append(", ");
            sb.Append(nameof(IsLastInWindowGroup)).Append("=").Append(IsLastInWindowGroup);
            sb.Append(", ");
            sb.Append(nameof(TraceId)).Append("=").Append(TraceId);
            sb.Append(", ");
            sb.Append(nameof(MaxWindowSize)).Append("=").Append(MaxWindowSize);
            sb.Append(", ");
            sb.Append(nameof(AllOptions)).Append("=").Append(StringUtilities.StringifyOptions(AllOptions));
            sb.Append("}");
            return sb.ToString();
        }

        public void AddOption(string name, string value)
        {
            List<string> optionValues;
            if (AllOptions.ContainsKey(name))
            {
                optionValues = AllOptions[name];
            }
            else
            {
                optionValues = new List<string>();
                AllOptions.Add(name, optionValues);
            }
            optionValues.Add(value);
        }

        public void ParseKnownOptions()
        {
            // Purpose of this method includes "syncing" properties for known options with AllOptions.
            // So reset before parsing.
            IdleTimeout = null;
            AbortCode = null;
            IsLastInWindow = null;
            IsWindowFull = null;
            IsLastInWindowGroup = null;
            TraceId = null;
            MaxWindowSize = null;

            // Now identify and validate known options.
            // In case of repetition, last one wins.
            foreach (var name in AllOptions.Keys)
            {
                var values = AllOptions[name];
                if (values.Count == 0)
                {
                    continue;
                }
                var value = values[values.Count - 1];
                try
                {
                    switch (name)
                    {
                        case OptionNameIdleTimeout:
                            IdleTimeout = ParseOptionAsInt32(value);
                            break;
                        case OptionNameAbortCode:
                            AbortCode = ParseOptionAsInt32(value);
                            break;
                        case OptionNameIsLastInWindow:
                            IsLastInWindow = ParseOptionAsBoolean(value);
                            break;
                        case OptionNameIsWindowFull:
                            IsWindowFull = ParseOptionAsBoolean(value);
                            break;
                        case OptionNameIsLastInWindowGroup:
                            IsLastInWindowGroup = ParseOptionAsBoolean(value);
                            break;
                        case OptionNameTraceId:
                            TraceId = value;
                            break;
                        case OptionNameMaxWindowSize:
                            MaxWindowSize = ParseOptionAsInt32(value);
                            break;
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Received invalid value for option {name}={value}", ex);
                }
            }
        }

        internal static int ParseOptionAsInt32(string optionValue)
        {
            return int.Parse(optionValue);
        }

        internal static bool ParseOptionAsBoolean(string optionValue)
        {
            switch (optionValue.ToLowerInvariant())
            {
                case "true":
                    return true;
                case "false":
                    return false;
            }
            throw new Exception($"expected {true} or {false}");
        }

        public IEnumerable<string[]> GenerateList()
        {
            var knownOptions = GatherKnownOptions();

            // store usages of known options when iterating over AllOptions.
            // will need it in the end
            var knownOptionsUsed = new HashSet<string>();
            
            foreach (var kvp in AllOptions)
            {
                string lastValue = null;
                foreach (var optionValue in kvp.Value)
                {
                    yield return new string[] { kvp.Key, optionValue };
                    lastValue = optionValue;
                }

                // override with known options if defined differently from last value in
                // AllOptions.
                if (knownOptions.ContainsKey(kvp.Key))
                {
                    // only send out known value if it is different from the last value in
                    // AllOptions to avoid unnecessary duplication.
                    string overridingValue = knownOptions[kvp.Key];

                    // mark as used, even if not sent out.
                    knownOptionsUsed.Add(kvp.Key);

                    // ignore letter case for boolean options.
                    bool isDefinedDifferently;
                    if (lastValue == null)
                    {
                        isDefinedDifferently = true;
                    }
                    else
                    {
                        if (kvp.Key == OptionNameIsLastInWindow || kvp.Key == OptionNameIsLastInWindowGroup ||
                            kvp.Key == OptionNameIsWindowFull)
                        {
                            isDefinedDifferently = !lastValue.Equals(overridingValue, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            isDefinedDifferently = lastValue != overridingValue;
                        }
                    }
                    if (isDefinedDifferently)
                    {
                        yield return new string[] { kvp.Key, overridingValue };
                    }
                }
            }

            // Just in case AllOptions doesn't contain a known option,
            // deal with that possibility here.
            foreach (var kvp in knownOptions)
            {
                if (!knownOptionsUsed.Contains(kvp.Key))
                {
                    yield return new string[] { kvp.Key, kvp.Value };
                }
            }
        }

        private Dictionary<string, string> GatherKnownOptions()
        {
            // for predictability of test results, gather in lexicographical order.
            var knownOptions = new Dictionary<string, string>();
            if (AbortCode != null)
            {
                knownOptions.Add(OptionNameAbortCode, AbortCode.ToString());
            }
            if (IdleTimeout != null)
            {
                knownOptions.Add(OptionNameIdleTimeout, IdleTimeout.ToString());
            }
            if (IsLastInWindow != null)
            {
                // NB: for some reason, C# outputs capitalized True or False for stringified booleans.
                knownOptions.Add(OptionNameIsLastInWindow, IsLastInWindow.ToString());
            }
            if (IsLastInWindowGroup != null)
            {
                knownOptions.Add(OptionNameIsLastInWindowGroup, IsLastInWindowGroup.ToString());
            }
            if (IsWindowFull != null)
            {
                knownOptions.Add(OptionNameIsWindowFull, IsWindowFull.ToString());
            }
            if (TraceId != null)
            {
                knownOptions.Add(OptionNameTraceId, TraceId);
            }
            if (MaxWindowSize != null)
            {
                knownOptions.Add(OptionNameMaxWindowSize, MaxWindowSize.ToString());
            }
            return knownOptions;
        }

        public void TransferParsedKnownOptionsTo(ProtocolDatagramOptions destOptions)
        {
            if (IdleTimeout != null)
            {
                destOptions.IdleTimeout = IdleTimeout;
            }
            if (AbortCode != null)
            {
                destOptions.AbortCode = AbortCode;
            }
            if (IsLastInWindow != null)
            {
                destOptions.IsLastInWindow = IsLastInWindow;
            }
            if (IsWindowFull != null)
            {
                destOptions.IsWindowFull = IsWindowFull;
            }
            if (IsLastInWindowGroup != null)
            {
                destOptions.IsLastInWindowGroup = IsLastInWindowGroup;
            }
            if (TraceId != null)
            {
                destOptions.TraceId = TraceId;
            }
            if (MaxWindowSize != null)
            {
                destOptions.MaxWindowSize = MaxWindowSize;
            }
        }
    }
}
