using Cadence.Kernel.Domain.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Maps <see cref="IBitemporalEntity.Period"/> to the <c>valid_period</c> and <c>recorded_period</c>
/// <c>tstzrange</c> columns (spec §4.1, ADR-2246). Every bitemporal table also needs
/// <see cref="BitemporalMigrationBuilderExtensions.CreateBitemporalGuards"/> in its migration (K-TIME-1, K-TIME-2).
/// </summary>
internal static class BitemporalEntityTypeBuilderExtensions
{
    public const string ValidPeriodColumn = "valid_period";
    public const string RecordedPeriodColumn = "recorded_period";

    public static EntityTypeBuilder<T> HasBitemporalPeriod<T>(this EntityTypeBuilder<T> builder)
        where T : class, IBitemporalEntity
    {
        builder.ComplexProperty<TemporalPeriod>(nameof(IBitemporalEntity.Period), period =>
        {
            period.Property(p => p.Valid)
                .HasConversion<TimeRangeValueConverter>()
                .HasColumnName(ValidPeriodColumn)
                .HasColumnType("tstzrange")
                .IsRequired();
            period.Property(p => p.Recorded)
                .HasConversion<TimeRangeValueConverter>()
                .HasColumnName(RecordedPeriodColumn)
                .HasColumnType("tstzrange")
                .IsRequired();
        });

        return builder;
    }
}
