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
        public const string OptionNameIsWindowFull = KnownOptionPrefix + "window_full";
        public const string OptionNameIsLastInWindow = KnownOptionPrefix + "last_in_window";
        public const string OptionNameIsLastInWindowGroup = KnownOptionPrefix + "last_in_window_group";
        public const string OptionNameTraceId = KnownOptionPrefix + "traceId";

        public ProtocolDatagramOptions()
        {
            // use of Dictionary is part of reference implementation API to ensure that key insertion order 
            // is the same as key retrieval order.
            AllOptions = new Dictionary<string, List<string>>();
        }

        public IDictionary<string, List<string>> AllOptions { get; }

        // Known options.
        public int? IdleTimeoutSecs { get; set; }
        public int? AbortCode { get; set; }
        public bool? IsWindowFull { get; set; }
        public bool? IsLastInWindow { get; set; }
        public bool? IsLastInWindowGroup { get; set; }
        public string TraceId { get; set; }

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
                            IdleTimeoutSecs = ParseOptionAsInt32(value);
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
                    string overridingValue = knownOptions[kvp.Key];
                    // only send out known value if it is different from the last value in
                    // AllOptions to avoid unnecessary duplication.
                    if (lastValue == null || lastValue != overridingValue)
                    {
                        yield return new string[] { kvp.Key, overridingValue };
                        knownOptionsUsed.Add(kvp.Key);
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
            var knownOptions = new Dictionary<string, string>();
            if (IdleTimeoutSecs != null)
            {
                knownOptions.Add(OptionNameIdleTimeout, IdleTimeoutSecs.ToString());
            }
            if (AbortCode != null)
            {
                knownOptions.Add(OptionNameAbortCode, AbortCode.ToString());
            }
            if (IsLastInWindow != null)
            {
                knownOptions.Add(OptionNameIsLastInWindow, IsLastInWindow.ToString());
            }
            if (IsWindowFull != null)
            {
                knownOptions.Add(OptionNameIsWindowFull, IsWindowFull.ToString());
            }
            if (IsLastInWindowGroup != null)
            {
                knownOptions.Add(OptionNameIsLastInWindowGroup, IsLastInWindowGroup.ToString());
            }
            if (TraceId != null)
            {
                knownOptions.Add(OptionNameTraceId, TraceId);
            }
            return knownOptions;
        }

        public void TransferKnownOptions(ProtocolDatagramOptions destOptions)
        {
            if (IdleTimeoutSecs != null)
            {
                destOptions.IdleTimeoutSecs = IdleTimeoutSecs;
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
        }
    }
}
