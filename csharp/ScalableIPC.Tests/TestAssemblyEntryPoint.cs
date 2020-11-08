using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;
using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

// Picked from: https://github.com/xunit/samples.xunit/blob/main/AssemblyFixtureExample/Samples.cs
[assembly: Xunit.TestFramework("ScalableIPC.Tests.TestAssemblyEntryPoint", "ScalableIPC.Tests")]

namespace ScalableIPC.Tests
{
    class TestAssemblyEntryPoint
    {
        public static TestConfiguration Config { get; set; }

        public TestAssemblyEntryPoint()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile(path: "AppSettings.json").Build();
            NLog.Extensions.Logging.ConfigSettingLayoutRenderer.DefaultConfiguration = config;
            Config = config.Get<TestConfiguration>();
            CustomLoggerFacade.Logger = new TestLogger();

            ResetDb();
        }

        private void ResetDb()
        {
            using (SqlConnection conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("DELETE FROM [dbo].[NLog] WHERE [App] = 'scalableipc'", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        internal static T AccessDb<T>(Func<IDbConnection, T> dbProc)
        {
            using (IDbConnection conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                return dbProc.Invoke(conn);
            }
        }
    }

    class TestLogger : ICustomLogger
    {
        public bool Enabled => true;

        public void Log(CustomLogEvent logEvent)
        {
            var logger = LogManager.GetCurrentClassLogger().Info()
                .Message(logEvent.Message ?? "");
            if (logEvent.Data != null)
            {
                foreach (var k in logEvent.Data)
                {
                    logger = logger.Property(k.Key, k.Value);
                }
            }
            var allProps = JObject.FromObject(logEvent.Data ?? new Dictionary<string, object>());
            if (logEvent.LogPosition != null)
            {
                logger = logger.Property("LogPosition", logEvent.LogPosition);
                allProps.Add("LogPosition", logEvent.LogPosition);
            }
            logger = logger.Property("AllProps", allProps.ToString(Formatting.None));
            logger = logger.Exception(logEvent.Error);
            logger.Write();
        }
    }
}
