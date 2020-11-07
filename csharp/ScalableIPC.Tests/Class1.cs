using Dapper;
using NLog;
using NLog.Fluent;
using System;
using System.Linq;
using Xunit;

namespace ScalableIPC.Tests
{
    public class Class1
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public void PassingTest()
        {
            _logger.Info()
                .Message("InSIDE PASING test")
                .Property("LogPosition", "e079bb21-0c66-435c-81ce-75a455497971")
                .Property("AllProps", "work")
                .Write();
            var results = TestAssemblyEntryPoint.AccessDb(conn =>
            {
                return conn.Query<TestLogRecord>("SELECT * FROM NLog ORDER BY Id").ToList();
            });

            Assert.Single(results);
            Assert.Equal("e079bb21-0c66-435c-81ce-75a455497971", results[0].LogPosition);
            Assert.NotNull(results[0].Properties);
            Assert.Contains("work", results[0].Properties);
        }
    }
}
