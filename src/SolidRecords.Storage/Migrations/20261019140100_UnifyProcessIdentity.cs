using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolidRecords.Storage.Migrations;

public partial class UnifyProcessIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "recording_id", table: "activity_leaves", type: "TEXT", nullable: false, defaultValue: "");
        // Preserve recording boundaries before reparenting windows. Leaf/span/window IDs never change.
        migrationBuilder.Sql("""
            UPDATE activity_leaves SET recording_id = (
                SELECT p.recording_id FROM window_activities w
                JOIN process_activities p ON p.id = w.process_activity_id
                WHERE w.id = activity_leaves.window_activity_id);
            CREATE TEMP TABLE process_identity_map AS
                SELECT id AS old_id, FIRST_VALUE(id) OVER (
                    PARTITION BY process_id, process_start_ticks ORDER BY start, id) AS canonical_id
                FROM process_activities WHERE process_start_ticks IS NOT NULL;
            CREATE UNIQUE INDEX ix_identity_map_old ON process_identity_map(old_id);
            CREATE INDEX ix_identity_map_canonical ON process_identity_map(canonical_id);
            UPDATE process_activities AS canonical SET
                start = (SELECT MIN(p.start) FROM process_activities p
                    JOIN process_identity_map m ON m.old_id = p.id WHERE m.canonical_id = canonical.id),
                end = (SELECT MAX(p.end) FROM process_activities p
                    JOIN process_identity_map m ON m.old_id = p.id WHERE m.canonical_id = canonical.id),
                is_open = (SELECT MAX(p.is_open) FROM process_activities p
                    JOIN process_identity_map m ON m.old_id = p.id WHERE m.canonical_id = canonical.id)
                WHERE id IN (SELECT canonical_id FROM process_identity_map);
            UPDATE window_activities SET process_activity_id = (
                SELECT canonical_id FROM process_identity_map WHERE old_id = window_activities.process_activity_id)
                WHERE process_activity_id IN (SELECT old_id FROM process_identity_map);
            DELETE FROM process_activities WHERE id IN (
                SELECT old_id FROM process_identity_map WHERE old_id != canonical_id);
            DROP TABLE process_identity_map;
            """);
        migrationBuilder.CreateIndex(
            name: "ix_process_identity", table: "process_activities",
            columns: new[] { "process_id", "process_start_ticks" }, unique: true,
            filter: "process_start_ticks IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Zlúčenie pozorovaní procesov nemožno vrátiť bez rozdelenia histórie.");
}
