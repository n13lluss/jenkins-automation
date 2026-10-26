using Microsoft.EntityFrameworkCore;

namespace PipelineForge.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Preset> Presets => Set<Preset>();
    public DbSet<PresetVersion> PresetVersions => Set<PresetVersion>();
    public DbSet<PipelineProject> Projects => Set<PipelineProject>();
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Subject).IsUnique();
            e.Property(x => x.Subject).HasMaxLength(512);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });
        b.Entity<Workspace>(e => e.Property(x => x.Name).HasMaxLength(100));
        b.Entity<Membership>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Connection>(e =>
        {
            e.HasIndex(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.BaseUrl).HasMaxLength(2048);
            e.Property(x => x.SecretRef).HasMaxLength(512);
        });
        b.Entity<Preset>(e =>
        {
            e.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.PresetId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
        });
        b.Entity<PresetVersion>(e => e.HasKey(x => new { x.PresetId, x.Version }));
        b.Entity<PipelineProject>(e =>
        {
            e.HasIndex(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        b.Entity<GenerationRun>(e =>
        {
            e.HasIndex(x => new { x.WorkspaceId, x.ProjectId });
            e.HasOne<PipelineProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditEvent>(e =>
        {
            e.HasIndex(x => new { x.WorkspaceId, x.At });
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Target).HasMaxLength(500);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        GuardAuditLog();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditLog();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void GuardAuditLog()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("The audit log is append-only.");
    }
}
