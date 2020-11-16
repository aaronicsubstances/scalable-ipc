using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public class VoidType
    {
        private VoidType() { }

        public static readonly VoidType Instance = new VoidType();
    }
}
