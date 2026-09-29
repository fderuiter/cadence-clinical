using System;

using Cadence.Testing;
using Cadence.Testing.Factories;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Testing;

public class BogusEntityFactoryTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SubjectFactory_CreatesDefaultSubjectWithBogusId()
    {
        var subject1 = SubjectFactory.Create();
        var subject2 = SubjectFactory.Create();

        subject1.Should().NotBeNull();
        subject1.Id.Should().StartWith("SUBJ-");
        subject1.StudyId.Should().Be("study_oncology_phase3");
        subject1.SiteId.Should().Be("SITE-101");
        subject1.Status.Should().Be("ENROLLED");
        subject1.ProtocolVersion.Should().Be("1.0");
        subject1.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        subject1.Id.Should().NotBe(subject2.Id);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SubjectFactory_AllowsPropertyOverrides()
    {
        var subject = SubjectFactory.Create(
            id: "SUBJ-999",
            studyId: "custom_study",
            siteId: "SITE-200",
            status: "SCREENED"
        );

        subject.Id.Should().Be("SUBJ-999");
        subject.StudyId.Should().Be("custom_study");
        subject.SiteId.Should().Be("SITE-200");
        subject.Status.Should().Be("SCREENED");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void ProtocolDefinitionFactory_CreatesValidProtocolRecord()
    {
        var protocol = ProtocolDefinitionFactory.Create();

        protocol.Should().NotBeNull();
        protocol.Id.Should().StartWith("PROT-");
        protocol.ProtocolTitle.Should().Contain("Oncology");
        protocol.ProtocolVersion.Should().Be("1.0");
        protocol.IsActive.Should().BeTrue();
        protocol.Arms.Should().HaveCount(2);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void ClinicalObservationFactory_CreatesValidObservationRecord()
    {
        var obs = ClinicalObservationFactory.Create();

        obs.Should().NotBeNull();
        obs.Id.Should().StartWith("OBS-");
        obs.SubjectId.Should().Be("SUBJ-001");
        obs.ItemGroupOid.Should().Be("VS");
        obs.ItemOid.Should().Be("SYSBP");
        obs.Value.Should().Be("120");
        obs.Status.Should().Be("VALID");
        obs.VersionIndex.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void QueryDiscrepancyFactory_CreatesValidQueryRecord()
    {
        var query = QueryDiscrepancyFactory.Create();

        query.Should().NotBeNull();
        query.Id.Should().StartWith("QRY-");
        query.SubjectId.Should().Be("SUBJ-001");
        query.FieldName.Should().Be("SYSBP");
        query.Status.Should().Be("OPEN");
        query.CreatedBy.Should().Be("cra.monitor@example.com");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void ConsentRecordFactory_CreatesValidConsentRecord()
    {
        var consent = ConsentRecordFactory.Create();

        consent.Should().NotBeNull();
        consent.Id.Should().StartWith("CONSENT-");
        consent.SubjectPseudonym.Should().Be("SUBJ-001");
        consent.SignatureManifest.Should().ContainKey("algorithm");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void DocumentMetadataFactory_CreatesValidDocumentMetadataRecord()
    {
        var doc = DocumentMetadataFactory.Create();

        doc.Should().NotBeNull();
        doc.Id.Should().StartWith("DOC-");
        doc.Filename.Should().EndWith(".pdf");
        doc.Zone.Should().Be("01_TRIAL_MANAGEMENT");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AuditLogFactory_CreatesValid21CfrPart11AuditRecord()
    {
        var audit = AuditLogFactory.Create();

        audit.Should().NotBeNull();
        audit.Id.Should().StartWith("AUDIT-");
        audit.UserId.Should().Be("site.crc@example.com");
        audit.Action.Should().Be("UPDATE_OBSERVATION");
        audit.ResourceType.Should().Be("ClinicalObservation");
        audit.ReasonForChange.Should().NotBeNullOrEmpty();
        audit.VersionIndex.Should().Be(2);
    }
}
