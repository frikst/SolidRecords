using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolidRecords.Storage.Migrations;

/// <summary>Creates new databases and adopts the unchanged version 1–4 table layout.</summary>
public partial class AdoptExistingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing installations have no EF history. Preserve their tables and rows.
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS application_icons (
                application TEXT PRIMARY KEY COLLATE NOCASE, png BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, kind TEXT NOT NULL,
                timestamp INTEGER NOT NULL, process_id INTEGER, application TEXT,
                window_handle INTEGER, title TEXT, url TEXT, reason TEXT);
            CREATE INDEX IF NOT EXISTS ix_events_timestamp ON events(timestamp);
            CREATE TABLE IF NOT EXISTS process_activities (
                id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, process_id INTEGER NOT NULL,
                application TEXT NOT NULL, process_start_ticks INTEGER, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE TABLE IF NOT EXISTS window_activities (
                id TEXT PRIMARY KEY, process_activity_id TEXT NOT NULL REFERENCES process_activities(id),
                handle INTEGER NOT NULL, title TEXT NOT NULL, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE TABLE IF NOT EXISTS activity_leaves (
                id TEXT PRIMARY KEY, window_activity_id TEXT NOT NULL REFERENCES window_activities(id),
                title TEXT NOT NULL, url TEXT, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE INDEX IF NOT EXISTS ix_process_time ON process_activities(start,end);
            CREATE INDEX IF NOT EXISTS ix_window_time ON window_activities(start,end);
            CREATE INDEX IF NOT EXISTS ix_window_parent ON window_activities(process_activity_id);
            CREATE INDEX IF NOT EXISTS ix_leaf_time ON activity_leaves(start,end);
            CREATE INDEX IF NOT EXISTS ix_leaf_parent ON activity_leaves(window_activity_id);
            CREATE TABLE IF NOT EXISTS activity_spans (
                id TEXT PRIMARY KEY,activity_id TEXT NOT NULL REFERENCES activity_leaves(id) ON DELETE CASCADE,
                start INTEGER NOT NULL,end INTEGER NOT NULL,CHECK(end >= start));
            CREATE INDEX IF NOT EXISTS ix_span_parent ON activity_spans(activity_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Prevzatie existujúcej histórie nemožno vrátiť odstránením tabuliek.");
}
