using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Concurrency
{
    public class LogicalThreadTerminatedException : TaskCanceledException
    {
        public LogicalThreadTerminatedException(Task task) : base(task)
        {
        }
    }
}
