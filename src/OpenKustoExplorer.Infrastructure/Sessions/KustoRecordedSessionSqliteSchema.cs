using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Creates and versions the recorded-session SQLite schema.
/// </summary>
internal static class KustoRecordedSessionSqliteSchema
{
    private const int CurrentVersion = 1;
    private const string CreateSchemaSql = """
        CREATE TABLE recorded_sessions (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            created_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL
        );

        CREATE TABLE recording_periods (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            stopped_at_utc TEXT NULL,
            FOREIGN KEY (session_id) REFERENCES recorded_sessions(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_executions (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL,
            period_id TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            document_id TEXT NOT NULL,
            document_title TEXT NOT NULL,
            display_name TEXT NULL,
            cluster_uri TEXT NOT NULL,
            database_name TEXT NOT NULL,
            query_text TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            status INTEGER NOT NULL,
            error_message TEXT NULL,
            duration_milliseconds REAL NULL,
            completeness INTEGER NOT NULL,
            source_table_name TEXT NULL,
            is_composable INTEGER NOT NULL,
            UNIQUE (session_id, sequence),
            FOREIGN KEY (session_id) REFERENCES recorded_sessions(id) ON DELETE CASCADE,
            FOREIGN KEY (period_id) REFERENCES recording_periods(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_relation_columns (
            execution_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            result_column_name TEXT NOT NULL,
            source_column_name TEXT NOT NULL,
            PRIMARY KEY (execution_id, ordinal),
            FOREIGN KEY (execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_result_tables (
            id TEXT PRIMARY KEY,
            execution_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            name TEXT NOT NULL,
            UNIQUE (execution_id, ordinal),
            FOREIGN KEY (execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_result_columns (
            table_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            name TEXT NOT NULL,
            type_name TEXT NOT NULL,
            PRIMARY KEY (table_id, ordinal),
            FOREIGN KEY (table_id) REFERENCES recorded_result_tables(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_result_rows (
            id TEXT PRIMARY KEY,
            table_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            display_values_json TEXT NOT NULL,
            raw_values_json TEXT NOT NULL,
            row_key TEXT NOT NULL,
            UNIQUE (table_id, ordinal),
            FOREIGN KEY (table_id) REFERENCES recorded_result_tables(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_value_occurrences (
            execution_id TEXT NOT NULL,
            table_id TEXT NOT NULL,
            row_id TEXT NOT NULL,
            table_ordinal INTEGER NOT NULL,
            row_ordinal INTEGER NOT NULL,
            column_ordinal INTEGER NOT NULL,
            column_name TEXT NOT NULL,
            type_name TEXT NOT NULL,
            canonical_value TEXT NOT NULL,
            value_hash TEXT NOT NULL,
            display_text TEXT NOT NULL,
            is_null INTEGER NOT NULL,
            PRIMARY KEY (execution_id, table_ordinal, row_ordinal, column_ordinal),
            FOREIGN KEY (execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE,
            FOREIGN KEY (table_id) REFERENCES recorded_result_tables(id) ON DELETE CASCADE,
            FOREIGN KEY (row_id) REFERENCES recorded_result_rows(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_value_interests (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL,
            declared_execution_id TEXT NOT NULL,
            source INTEGER NOT NULL,
            column_name TEXT NOT NULL,
            type_name TEXT NOT NULL,
            canonical_value TEXT NOT NULL,
            value_hash TEXT NOT NULL,
            coordinate_execution_id TEXT NULL,
            table_ordinal INTEGER NULL,
            row_ordinal INTEGER NULL,
            column_ordinal INTEGER NULL,
            literal_start INTEGER NULL,
            literal_length INTEGER NULL,
            mark_id TEXT NULL,
            is_suppressed INTEGER NOT NULL,
            FOREIGN KEY (session_id) REFERENCES recorded_sessions(id) ON DELETE CASCADE,
            FOREIGN KEY (declared_execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE,
            FOREIGN KEY (mark_id) REFERENCES recorded_marks(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_marks (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            execution_id TEXT NOT NULL,
            table_ordinal INTEGER NOT NULL,
            row_ordinal INTEGER NOT NULL,
            column_ordinal INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE (session_id, kind, execution_id, table_ordinal, row_ordinal, column_ordinal),
            FOREIGN KEY (session_id) REFERENCES recorded_sessions(id) ON DELETE CASCADE,
            FOREIGN KEY (execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE
        );

        CREATE TABLE recorded_chain_endpoints (
            session_id TEXT NOT NULL,
            role INTEGER NOT NULL,
            execution_id TEXT NOT NULL,
            table_ordinal INTEGER NOT NULL,
            row_ordinal INTEGER NOT NULL,
            column_ordinal INTEGER NOT NULL,
            PRIMARY KEY (session_id, role),
            FOREIGN KEY (session_id) REFERENCES recorded_sessions(id) ON DELETE CASCADE,
            FOREIGN KEY (execution_id) REFERENCES recorded_executions(id) ON DELETE CASCADE
        );

        CREATE INDEX ix_recording_periods_session
            ON recording_periods(session_id, started_at_utc);
        CREATE INDEX ix_recorded_executions_session_sequence
            ON recorded_executions(session_id, sequence);
        CREATE INDEX ix_recorded_value_occurrences_identity
            ON recorded_value_occurrences(value_hash, type_name, canonical_value, execution_id);
        CREATE INDEX ix_recorded_value_occurrences_row
            ON recorded_value_occurrences(execution_id, table_ordinal, row_ordinal);
        CREATE INDEX ix_recorded_value_occurrences_row_id
            ON recorded_value_occurrences(row_id);
        CREATE INDEX ix_recorded_value_interests_identity
            ON recorded_value_interests(session_id, value_hash, type_name, canonical_value, is_suppressed);
        CREATE INDEX ix_recorded_value_interests_mark_id
            ON recorded_value_interests(mark_id);
        CREATE INDEX ix_recorded_marks_session
            ON recorded_marks(session_id, execution_id);

        PRAGMA user_version = 1;
        """;

    /// <summary>
    /// Creates the schema or rejects a database from a newer application version.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    internal static void EnsureCreated(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        if (version > CurrentVersion)
        {
            throw new KustoRecordedSessionDatabaseVersionException(version, CurrentVersion);
        }

        if (version == 0)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = CreateSchemaSql;
            schemaCommand.ExecuteNonQuery();
            transaction.Commit();
        }
    }
}
