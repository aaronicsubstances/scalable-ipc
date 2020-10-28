using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class VoidType
    {
        private VoidType() { }

        public static readonly VoidType Instance = new VoidType();
    }
}
