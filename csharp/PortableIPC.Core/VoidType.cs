using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core
{
    public class VoidType
    {
        private VoidType() { }

        public static readonly VoidType Instance = new VoidType();
    }
}
