using System;

namespace ScalableIPC.IntegrationTests.Helpers
{
    class TestLogRecord
    {
        public int Id { get; set; }
        public string App { get; set; }
        public string MachineName { get; set; }
        public DateTime LoggedAt { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Logger { get; set; }
        public string Properties { get; set; }
        public string Callsite { get; set; }
        public string Exception { get; set; }
    }
}