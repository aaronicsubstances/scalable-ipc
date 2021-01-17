using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;
using ScalableIPC.Core.Helpers;
using ScalableIPC.IntegrationTests.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

// Picked from: https://github.com/xunit/samples.xunit/blob/main/AssemblyFixtureExample/Samples.cs
[assembly: Xunit.TestFramework("ScalableIPC.IntegrationTests.TestAssemblyEntryPoint", "ScalableIPC.IntegrationTests")]

namespace ScalableIPC.IntegrationTests
{
    class TestAssemblyEntryPoint
    {
        public static TestConfiguration Config { get; set; }

        public TestAssemblyEntryPoint()
        {
            try
            {
                IConfigurationRoot config = new ConfigurationBuilder()
                    .AddJsonFile(path: "appsettings.json").Build();
                NLog.Extensions.Logging.ConfigSettingLayoutRenderer.DefaultConfiguration = config;
                Config = config.Get<TestConfiguration>();
                CustomLoggerFacade.Logger = new TestLogger();
                TestDatabase.ResetDb();
            }
            catch (Exception ex)
            {
                var errorTime = DateTime.Now;
                File.AppendAllText($"logs/{errorTime.ToString("yyyy-MM-dd")}.log",
                    $"{errorTime} Failed to initialize test project {ex}\n");
                Environment.Exit(1);
            }
        }
    }

    class TestLogger : ICustomLogger
    {
        private static readonly Logger _logger4TestEvts = LogManager.GetLogger(
            Assembly.GetExecutingAssembly().GetName().Name);
        private static readonly Logger _stdoutLogger = LogManager.GetLogger("StandardOutputEquivalent");

        public bool LogEnabled => true;
        public bool TestLogEnabled => _logger4TestEvts.IsDebugEnabled;

        public void Log(CustomLogEvent logEvent)
        {
            Logger logger4Evt = logEvent.TargetLogger != null ?
                LogManager.GetLogger(logEvent.TargetLogger) :
                LogManager.GetCurrentClassLogger();
            WriteLog(logger4Evt, logEvent, false);
        }

        public void TestLog(CustomLogEvent logEvent)
        {
            WriteLog(_logger4TestEvts, logEvent, true);
        }

        public void WriteToStdOut(string message, Exception ex)
        {
            _stdoutLogger.Debug(ex, message);
        }

        private void WriteLog(Logger logger, CustomLogEvent logEvent, bool forTest)
        {
            var logBuilder = forTest ? logger.Debug() : logger.Warn();
            logBuilder.Message(logEvent.Message ?? "")
                .Exception(logEvent.Error);
            var allProps = JObject.FromObject(logEvent.Data ?? new Dictionary<string, object>());
            logBuilder.Property("logData", allProps.ToString(Formatting.None));
            logBuilder.Property("targetLogger", logEvent.TargetLogger ?? logger.Name);
            logBuilder.Write();
        }
    }
}
