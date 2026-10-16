using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SolidRecords.Storage;

public sealed class ActivityDbContext(DbContextOptions<ActivityDbContext> options) : DbContext(options)
{
    internal DbSet<ProcessRow> Processes => Set<ProcessRow>();
    internal DbSet<WindowRow> Windows => Set<WindowRow>();
    internal DbSet<LeafRow> Leaves => Set<LeafRow>();
    internal DbSet<SpanRow> Spans => Set<SpanRow>();
    internal DbSet<IconRow> Icons => Set<IconRow>();
    internal DbSet<EventRow> Events => Set<EventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessRow>(entity =>
        {
            entity.ToTable("process_activities", table => table.HasCheckConstraint("ck_process_time", "end >= start"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Start, x.End }).HasDatabaseName("ix_process_time");
        });
        modelBuilder.Entity<WindowRow>(entity =>
        {
            entity.ToTable("window_activities", table => table.HasCheckConstraint("ck_window_time", "end >= start"));
            entity.HasKey(x => x.Id);
            entity.HasOne<ProcessRow>().WithMany().HasForeignKey(x => x.ProcessActivityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ProcessActivityId).HasDatabaseName("ix_window_parent");
            entity.HasIndex(x => new { x.Start, x.End }).HasDatabaseName("ix_window_time");
        });
        modelBuilder.Entity<LeafRow>(entity =>
        {
            entity.ToTable("activity_leaves", table => table.HasCheckConstraint("ck_leaf_time", "end >= start"));
            entity.HasKey(x => x.Id);
            entity.HasOne<WindowRow>().WithMany().HasForeignKey(x => x.WindowActivityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.WindowActivityId).HasDatabaseName("ix_leaf_parent");
            entity.HasIndex(x => new { x.Start, x.End }).HasDatabaseName("ix_leaf_time");
        });
        modelBuilder.Entity<SpanRow>(entity =>
        {
            entity.ToTable("activity_spans", table => table.HasCheckConstraint("ck_span_time", "end >= start"));
            entity.HasKey(x => x.Id);
            entity.HasOne<LeafRow>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.ActivityId).HasDatabaseName("ix_span_parent");
        });
        modelBuilder.Entity<IconRow>(entity =>
        {
            entity.ToTable("application_icons");
            entity.HasKey(x => x.Application);
            entity.Property(x => x.Application).UseCollation("NOCASE");
        });
        modelBuilder.Entity<EventRow>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Timestamp).HasDatabaseName("ix_events_timestamp");
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}

// Tooling only: creating a migration must never open the user's database.
public sealed class ActivityDbContextFactory : IDesignTimeDbContextFactory<ActivityDbContext>
{
    public ActivityDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ActivityDbContext>()
        .UseSqlite("Data Source=artifacts/ef-design.db").Options);
}

internal sealed class ProcessRow
{
    public string Id { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public int ProcessId { get; set; }
    public string Application { get; set; } = "";
    public long? ProcessStartTicks { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public bool IsOpen { get; set; }
}

internal sealed class WindowRow
{
    public string Id { get; set; } = "";
    public string ProcessActivityId { get; set; } = "";
    public long Handle { get; set; }
    public string Title { get; set; } = "";
    public long Start { get; set; }
    public long End { get; set; }
    public bool IsOpen { get; set; }
}

internal sealed class LeafRow
{
    public string Id { get; set; } = "";
    public string WindowActivityId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public bool IsOpen { get; set; }
}

internal sealed class SpanRow
{
    public string Id { get; set; } = "";
    public string ActivityId { get; set; } = "";
    public long Start { get; set; }
    public long End { get; set; }
}

internal sealed class IconRow
{
    public string Application { get; set; } = "";
    public byte[] Png { get; set; } = [];
}

internal sealed class EventRow
{
    public string Id { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public string Kind { get; set; } = "";
    public long Timestamp { get; set; }
    public int? ProcessId { get; set; }
    public string? Application { get; set; }
    public long? WindowHandle { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Reason { get; set; }
}
