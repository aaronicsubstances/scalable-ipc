using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public interface AbstractEventLoopApi
    {
        void PostCallback(StoredCallback cb);
        void PostCallbackSerially(ISessionHandler sessionHandler, StoredCallback cb);
    }
}
