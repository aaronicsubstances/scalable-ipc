using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    /// <summary>
    /// The event loop abstraction is key to providing a library API which can be implemented without
    /// presuming the use of any concurrency or I/O programming model. In particular, it supports 3 models:
    /// 1. blocking I/O and multi-threaded
    /// 2. non-blocking I/O and single-threaded
    /// 3. non-blocking I/O and multi-threaded
    /// Blocking I/O model however presumes that event loop will run in dedicated threads not shared with other parts of
    /// an application using blocking I/O.
    /// </summary>
    public interface AbstractEventLoopApi
    {
        void PostCallback(ISessionHandler sessionHandler, Action cb);
        void PostCallbackSerially(ISessionHandler sessionHandler, Action cb);
    }
}
