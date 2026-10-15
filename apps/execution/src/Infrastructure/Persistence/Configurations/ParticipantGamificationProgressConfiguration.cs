using Cadence.Apps.Execution.Domain.Gamification;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ParticipantGamificationProgress"/> table.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed class ParticipantGamificationProgressConfiguration : IEntityTypeConfiguration<ParticipantGamificationProgress>
{
    public void Configure(EntityTypeBuilder<ParticipantGamificationProgress> builder)
    {
        builder.ToTable("participant_gamification_progresses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TotalBonusEarned).HasPrecision(18, 4).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MilestonesAchievedJson).HasColumnType("text").IsRequired();

        builder.HasIndex(x => new { x.SubjectId, x.StudyId }).IsUnique();
    }
}
