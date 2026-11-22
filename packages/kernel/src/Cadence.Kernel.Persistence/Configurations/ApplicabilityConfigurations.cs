using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.applicabilities</c> (spec §7.5, K-TIME-6, K-TIME-8).
/// </summary>
internal sealed class ApplicabilityConfiguration : IEntityTypeConfiguration<Applicability>
{
    public void Configure(EntityTypeBuilder<Applicability> builder)
    {
        builder.ToTable("applicabilities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InconsistencyReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.ParticipantId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.EnrollmentId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.EnrollmentId, x.ParticipantId, x.StudyId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Participant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudySite>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudySiteId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GoverningVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ApplicabilityId, x.EnrollmentId, x.ParticipantId, x.StudyId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.EnrollmentId, x.ParticipantId, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasMany(x => x.PendingTransitions).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ApplicabilityId, x.EnrollmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.EnrollmentId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.PendingTransitions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.applicability_periods</c> (spec §7.5, K-TIME-1, K-TIME-2, K-TIME-6).
/// </summary>
internal sealed class ApplicabilityPeriodConfiguration : IEntityTypeConfiguration<ApplicabilityPeriod>
{
    public void Configure(EntityTypeBuilder<ApplicabilityPeriod> builder)
    {
        builder.ToTable("applicability_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Basis).HasMaxLength(64).IsRequired();
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProtocolVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Effectivity>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EffectivityId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConsentStatus>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConsentStatusId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.ParticipantId });
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId });
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.applicability_pending_transitions</c> (spec §7.5, PRD-SUB-007).
/// </summary>
internal sealed class PendingApplicabilityTransitionConfiguration : IEntityTypeConfiguration<PendingApplicabilityTransition>
{
    public void Configure(EntityTypeBuilder<PendingApplicabilityTransition> builder)
    {
        builder.ToTable("applicability_pending_transitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FromVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TargetVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Effectivity>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EffectivityId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId, x.TargetVersionId });
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.consent_statuses</c> (spec §9.4, K-CONS-1).
/// </summary>
internal sealed class ConsentStatusConfiguration : IEntityTypeConfiguration<ConsentStatus>
{
    public void Configure(EntityTypeBuilder<ConsentStatus> builder)
    {
        builder.ToTable("consent_statuses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BasisKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId, x.ProtocolVersionId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.ParticipantId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.EnrollmentId, x.ProtocolVersionId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Participant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudySite>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudySiteId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProtocolVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ArtifactVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ArtifactVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ConsentStatusId, x.EnrollmentId, x.ProtocolVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.EnrollmentId, x.ProtocolVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.consent_status_periods</c> (spec §9.4, K-TIME-1, K-TIME-2).
/// </summary>
internal sealed class ConsentStatusPeriodConfiguration : IEntityTypeConfiguration<ConsentStatusPeriod>
{
    public void Configure(EntityTypeBuilder<ConsentStatusPeriod> builder)
    {
        builder.ToTable("consent_status_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.BasisKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Scopes)
            .HasColumnType("jsonb")
            .HasConversion(ConsentScopesJson.Converter, ConsentScopesJson.Comparer)
            .IsRequired();
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId, x.ProtocolVersionId });
    }

    private static class ConsentScopesJson
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public static readonly ValueConverter<IReadOnlyList<ConsentScopeEntry>, string> Converter = new(
            scopes => JsonSerializer.Serialize(scopes, Options),
            json => JsonSerializer.Deserialize<List<ConsentScopeEntry>>(json, Options) ?? new List<ConsentScopeEntry>());

        public static readonly ValueComparer<IReadOnlyList<ConsentScopeEntry>> Comparer = new(
            (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
            scopes => JsonSerializer.Serialize(scopes, Options).GetHashCode(StringComparison.Ordinal),
            scopes => (IReadOnlyList<ConsentScopeEntry>)(JsonSerializer.Deserialize<List<ConsentScopeEntry>>(JsonSerializer.Serialize(scopes, Options), Options) ?? new List<ConsentScopeEntry>()));
    }
}
