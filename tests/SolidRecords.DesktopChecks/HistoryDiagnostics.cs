using System.IO;
using Microsoft.Data.Sqlite;

namespace SolidRecords.DesktopChecks;

/// <summary>Read-only inspection; deliberately never calls the store's initialization or recovery.</summary>
internal static class HistoryDiagnostics
{
    public static int Run()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolidRecords", "activity.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        Console.WriteLine("Database: " + path);
        int schemaVersion;
        using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText = "SELECT sqlite_version();";
            Console.WriteLine("SQLite engine: " + metadata.ExecuteScalar());
            metadata.CommandText = "PRAGMA journal_mode;";
            Console.WriteLine("Journal mode: " + metadata.ExecuteScalar());
            metadata.CommandText = "PRAGMA page_count;";
            Console.WriteLine("Logical pages including WAL: " + metadata.ExecuteScalar());
            metadata.CommandText = "PRAGMA user_version;";
            schemaVersion = Convert.ToInt32(metadata.ExecuteScalar());
            Console.WriteLine("Schema version: " + schemaVersion);
        }
        // SQLite's backup API captures committed DB+WAL state consistently while the app runs.
        // Never copy just the .db file or run recovery/checkpoint commands on the user's database.
        var snapshotPath = Path.GetFullPath(Path.Combine("artifacts", "database-diagnostics", $"snapshot-{Guid.NewGuid():N}.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        using (var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath, Pooling = false
        }.ToString()))
        {
            snapshot.Open();
            connection.BackupDatabase(snapshot);
            using var check = snapshot.CreateCommand();
            check.CommandText = "PRAGMA quick_check(5);";
            Console.WriteLine("Consistent diagnostic snapshot: " + snapshotPath);
            using var result = check.ExecuteReader();
            while (result.Read()) Console.WriteLine("Snapshot integrity: " + result.GetString(0));
        }
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check(5);";
        Console.WriteLine("Read-only integrity check (application SQLite provider):");
        var healthy = true;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var result = reader.GetString(0);
                Console.WriteLine(result);
                healthy &= result.Equals("ok", StringComparison.OrdinalIgnoreCase);
            }
        if (!healthy)
        {
            Console.Error.WriteLine("This database view failed integrity validation; cause is undetermined. No repairs or data changes were performed.");
            return 2;
        }
        if (schemaVersion is not (2 or 3))
        {
            Console.WriteLine("Integrity is OK. Activity inspection skipped: this is not the version-2 schema used by the current application.");
            return 0;
        }
        command.CommandText = """
            SELECT p.application,l.title,COUNT(*),ROUND(SUM(l.end-l.start)/1000.0,1)
            FROM activity_leaves l JOIN window_activities w ON w.id=l.window_activity_id
            JOIN process_activities p ON p.id=w.process_activity_id
            WHERE trim(l.title)='' OR lower(p.application) IN
                ('explorer','solidrecords.app','lockapp','logonui','shellexperiencehost',
                 'searchhost','startmenuexperiencehost','textinputhost')
            GROUP BY p.application,l.title ORDER BY COUNT(*) DESC LIMIT 30;
            """;
        using (var reader = command.ExecuteReader())
            while (reader.Read()) Console.WriteLine($"{reader.GetString(0)} | {reader.GetString(1)} | {reader.GetInt64(2)} rows | {reader.GetDouble(3)} s");
        return 0;
    }
}
