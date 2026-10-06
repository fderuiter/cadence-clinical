using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Designer.Persistence;

/// <summary>
/// EF Core DbContext for Designer service persistence.
/// </summary>
public class DesignerDbContext : DbContext
{
    public DesignerDbContext(DbContextOptions<DesignerDbContext> options)
        : base(options)
    {
    }

    public DbSet<EcrfFormSnapshot> FormSnapshots => Set<EcrfFormSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EcrfFormSnapshot>(entity =>
        {
            entity.HasKey(e => e.FormId);
            entity.Property(e => e.FormId).HasMaxLength(128);
            entity.Property(e => e.StudyId).HasMaxLength(128);
            entity.Property(e => e.UsdmJson).IsRequired();
            entity.Property(e => e.CrdtStateVector).IsRequired();
            entity.Property(e => e.CompilationStatus).HasMaxLength(64);
        });
    }
}
