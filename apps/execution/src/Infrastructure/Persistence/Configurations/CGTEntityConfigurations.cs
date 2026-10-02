using Cadence.Apps.Execution.Domain.Cgt;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class CgtChainOfCustodyConfiguration : IEntityTypeConfiguration<CgtChainOfCustody>
{
    public void Configure(EntityTypeBuilder<CgtChainOfCustody> builder)
    {
        builder.ToTable("cgt_chains_of_custody");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Din).HasMaxLength(255).IsRequired();
        builder.Property(x => x.BatchId).HasMaxLength(255);
        builder.Property(x => x.CollectionSiteId).HasMaxLength(255);
        builder.Property(x => x.ManufacturingFacilityId).HasMaxLength(255);
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CurrentStage).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UnlockToken).HasMaxLength(512);

        builder.HasIndex(x => new { x.StudyId, x.SubjectId }).IsUnique();
        builder.HasIndex(x => x.Din);
    }
}

public sealed class CgtMilestoneEventConfiguration : IEntityTypeConfiguration<CgtMilestoneEvent>
{
    public void Configure(EntityTypeBuilder<CgtMilestoneEvent> builder)
    {
        builder.ToTable("cgt_milestone_events");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Stage).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MilestoneType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.LocationName).HasMaxLength(255);
        builder.Property(x => x.FacilityId).HasMaxLength(255);
        builder.Property(x => x.TemperatureStatus).HasMaxLength(64);
        builder.Property(x => x.CustodianId).HasMaxLength(255);
        builder.Property(x => x.CustodianName).HasMaxLength(255);
        builder.Property(x => x.CustodianRole).HasMaxLength(128);
        builder.Property(x => x.Carrier).HasMaxLength(255);
        builder.Property(x => x.TrackingNumber).HasMaxLength(255);
        builder.Property(x => x.SealId).HasMaxLength(255);
        builder.Property(x => x.DryShipperId).HasMaxLength(255);

        builder.HasIndex(x => x.ChainOfCustodyId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CgtBedsideVerificationConfiguration : IEntityTypeConfiguration<CgtBedsideVerification>
{
    public void Configure(EntityTypeBuilder<CgtBedsideVerification> builder)
    {
        builder.ToTable("cgt_bedside_verifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Din).HasMaxLength(255).IsRequired();
        builder.Property(x => x.BatchId).HasMaxLength(255);
        builder.Property(x => x.PatientWristbandId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PrimaryVerifierId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PrimaryVerifierName).HasMaxLength(255);
        builder.Property(x => x.PrimaryVerifierRole).HasMaxLength(128);
        builder.Property(x => x.PrimarySignatureToken).HasMaxLength(512).IsRequired();
        builder.Property(x => x.SecondaryVerifierId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SecondaryVerifierName).HasMaxLength(255);
        builder.Property(x => x.SecondaryVerifierRole).HasMaxLength(128);
        builder.Property(x => x.SecondarySignatureToken).HasMaxLength(512).IsRequired();
        builder.Property(x => x.UnlockToken).HasMaxLength(512).IsRequired();

        builder.HasIndex(x => new { x.StudyId, x.SubjectId });
        builder.HasIndex(x => x.UnlockToken);
    }
}

public sealed class CgtDestructionCertificateConfiguration : IEntityTypeConfiguration<CgtDestructionCertificate>
{
    public void Configure(EntityTypeBuilder<CgtDestructionCertificate> builder)
    {
        builder.ToTable("cgt_destruction_certificates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CertificateNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubjectId).HasMaxLength(255);
        builder.Property(x => x.Din).HasMaxLength(255);
        builder.Property(x => x.LotNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UnitOfMeasure).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DestructionReason).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DestructionMethod).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DestructionFacility).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Witness1Id).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Witness1Name).HasMaxLength(255);
        builder.Property(x => x.Witness1Role).HasMaxLength(128);
        builder.Property(x => x.Witness1SignatureToken).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Witness2Id).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Witness2Name).HasMaxLength(255);
        builder.Property(x => x.Witness2Role).HasMaxLength(128);
        builder.Property(x => x.Witness2SignatureToken).HasMaxLength(512).IsRequired();
        builder.Property(x => x.CryptographicSeal).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.CertificateNumber).IsUnique();
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CgtVialReturnReconciliationConfiguration : IEntityTypeConfiguration<CgtVialReturnReconciliation>
{
    public void Configure(EntityTypeBuilder<CgtVialReturnReconciliation> builder)
    {
        builder.ToTable("cgt_vial_return_reconciliations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.KitNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VialIdentifier).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Disposition).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LoggedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CertificateId).HasMaxLength(255);

        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.VialIdentifier);
    }
}
