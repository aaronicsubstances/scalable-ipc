using System;

namespace PortableIPC.Core
{
    public class StoredCallback
    {
        public StoredCallback(Action<object> callback, object arg = default)
        {
            Callback = callback;
            Arg = arg;
        }

        public Action<object> Callback { get; }
        public object Arg { get; }
        public void Run()
        {
            Callback.Invoke(Arg);
        }
    }
}
