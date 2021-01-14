using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public class CustomLogEvent
    {
        public static readonly string LogDataKeyLogPositionId = "logPositionId";
        public static readonly string LogDataKeyCurrentLogicalThreadId = "currentLogicalThreadId";
        public static readonly string LogDataKeyEndingLogicalThreadId = "endingLogicalThreadId";
        public static readonly string LogDataKeyNewLogicalThreadId = "newLogicalThreadId";

        public delegate string CustomLogEventMessageGenerator(
            Func<string, string> jsonSerializerFunc, Func<string, string> stringifyFunc);

        public CustomLogEvent(Type targetLogger)
            : this(targetLogger,null, null)
        { }

        public CustomLogEvent(Type targetLogger, string message)
            : this(targetLogger, message, null)
        { }

        public CustomLogEvent(Type targetLogger, string message, Exception error)
        {
            Message = message;
            Error = error;
            TargetLogger = targetLogger?.FullName;
        }

        public string Message { get; set; }
        public List<object> Arguments { get; set; }
        public Exception Error { get; set; }
        public object Data { get; set; }
        public string TargetLogger { get; set; }

        public CustomLogEvent AddProperty(string name, object value)
        {
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            ((IDictionary<string, object>)Data).Add(name, value);
            return this;
        }

        public CustomLogEvent GenerateMessage(CustomLogEventMessageGenerator customLogEventMsgGenerator)
        {
            Message = customLogEventMsgGenerator.Invoke(
                path => FetchDataSliceAndStringify(path, true),
                path => FetchDataSliceAndStringify(path, false));
            return this;
        }

        private string FetchDataSliceAndStringify(string path, bool serializeAsJson)
        {
            object dataSlice = FetchDataSlice(path);
            if (serializeAsJson)
            {
                return SerializeAsJson(dataSlice);
            }
            else
            {
                return dataSlice?.ToString() ?? "";
            }
        }

        internal object FetchDataSlice(string path)
        {
            // split path and ensure no surrounding whitespace around 
            // individual segments.
            var pathSegments = path.Split('/');
            for (int i = 0; i < pathSegments.Length; i++)
            {
                pathSegments[i] = pathSegments[i].Trim();
            }

            object dataSlice = Data;
            foreach (var pathSegment in pathSegments)
            {
                if (dataSlice == null)
                {
                    break;
                }

                // skip empty path segments.
                if (pathSegment.Length == 0)
                {
                    continue;
                }

                if (dataSlice is IList<object> jsonArray)
                {
                    if (int.TryParse(pathSegment, out int index))
                    {
                        // Support Python style indexing.
                        if (Math.Abs(index) >= jsonArray.Count)
                        {
                            dataSlice = null;
                        }
                        else
                        {
                            if (index < 0)
                            {
                                index += jsonArray.Count;
                            }
                            dataSlice = jsonArray[index];
                        }
                        continue;
                    }

                    // let dictionary branch of code handle pathSegments which are ints 
                    // but correspond to data slices which
                    // are not list instances.
                }

                if (dataSlice is IDictionary<string, object> dataSliceDict)
                {
                    if (dataSliceDict.ContainsKey(pathSegment))
                    {
                        dataSlice = dataSliceDict[pathSegment];
                    }
                    else
                    {
                        dataSlice = null;
                    }
                }
                else
                {
                    dataSlice = GetTreeDataPropertyValue(dataSlice, pathSegment);
                }
            }
            return dataSlice;
        }

        protected virtual object GetTreeDataPropertyValue(object dataSlice, string pathSegment)
        {
            if (dataSlice == null)
            {
                return null;
            }
            try
            {
                // try dictionary access. Probably IDictionary<string, some_type_not_object>
                dynamic dataSliceDict = dataSlice;
                return dataSliceDict[pathSegment];
            }
            catch (Exception)
            {
                try
                {
                    // try declared property access
                    var propHandle = dataSlice.GetType().GetProperty(pathSegment);
                    return propHandle.GetValue(dataSlice);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        protected virtual string SerializeAsJson(object o)
        {
            return JsonConvert.SerializeObject(o);
        }
    }
}
