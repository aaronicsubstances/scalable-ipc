using Dapper;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace ScalableIPC.IntegrationTests.Helpers
{
    class TestDatabase
    {
        internal static void ResetDb()
        {
            using (var conn = new SqlConnection(TestAssemblyEntryPoint.Config.ConnectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("DELETE FROM Logs", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        internal static List<TestLogRecord> GetTestLogs(Func<TestLogRecord, bool> validateAndFilter)
        {
            return AccessDb(dbConn =>
            {
                var itemList = dbConn.Query<TestLogRecord>("SELECT * FROM Logs ORDER BY Id")
                    .Where(validateAndFilter)
                    .ToList();
                foreach (var item in itemList)
                {
                    item.ParsedProperties = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        item.Properties);
                }
                return itemList;
            });
        }

        internal static T AccessDb<T>(Func<IDbConnection, T> dbProc)
        {
            using (IDbConnection conn = new SqlConnection(TestAssemblyEntryPoint.Config.ConnectionString))
            {
                conn.Open();
                return dbProc.Invoke(conn);
            }
        }
    }
}
