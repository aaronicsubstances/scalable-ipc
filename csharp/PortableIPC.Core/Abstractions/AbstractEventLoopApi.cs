using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public interface AbstractEventLoopApi
    {
        void PostCallback(Action cb);
        void PostCallbackSerially(ISessionHandler sessionHandler, Action cb);
    }
}
