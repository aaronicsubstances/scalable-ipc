using NLog;
using NLog.Fluent;
using System;
using Xunit;

namespace ScalableIPC.Tests
{
    public class Class1
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public void PassingTest()
        {
            Core.CustomLoggerFacade.Log("e079bb21-0c66-435c-81ce-75a455497971", "InSIDE PASING test", "shul", "work");
            Assert.Equal(4, Add(2, 2));
        }

        [Fact]
        public void FailingTest()
        {
            _logger.Info("In failing test...");
            //Assert.Equal(5, Add(2, 2));
        }

        int Add(int x, int y)
        {
            return x + y;
        }
    }
}
