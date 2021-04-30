using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;

namespace OpenFTTH.EventSourcing.Postgres
{
    public class PostgresSequenceStore : ISequences
    {
        private readonly string _connectionString;
        private readonly string _schema;
        private readonly HashSet<string> _createSequenceIfNotExistsCheckDone = new();

        public PostgresSequenceStore(string connectionString, string schema)
        {
            _connectionString = connectionString;
            _schema = schema.ToLower();
        }

        public long GetNextVal(string sequenceName)
        {
            using (var conn = GetConnection() as NpgsqlConnection)
            {
                conn.Open();

                if (!_createSequenceIfNotExistsCheckDone.Contains(sequenceName))
                {
                    // create sequence if not exists
                    using var truncateCmd = new NpgsqlCommand($"CREATE SEQUENCE IF NOT EXISTS {_schema}.{sequenceName}", conn);

                    truncateCmd.ExecuteNonQuery();

                    _createSequenceIfNotExistsCheckDone.Add(sequenceName);
                }

                var nextValSql = $"SELECT nextval('{_schema}.{sequenceName}')";

                using var cmd = new NpgsqlCommand(nextValSql, conn);

                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        public void DropSequence(string sequenceName)
        {
            using (var conn = GetConnection() as NpgsqlConnection)
            {
                conn.Open();

                using var truncateCmd = new NpgsqlCommand($"DROP SEQUENCE IF EXISTS {_schema}.{sequenceName}", conn);

                truncateCmd.ExecuteNonQuery();
            }
        }

        private IDbConnection GetConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }
    }
}
