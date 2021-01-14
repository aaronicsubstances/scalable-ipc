using System.Reflection;
using Xunit;

namespace ScalableIPC.IntegrationTests.Helpers
{
    public class TestConfiguration
    {
        public string ConnectionString { get; set; }


        [Fact]
        public void TestAssemblyName()
        {
            Assert.Equal("ScalableIPC.IntegrationTests", Assembly.GetExecutingAssembly().GetName().Name);
        }
    }
}