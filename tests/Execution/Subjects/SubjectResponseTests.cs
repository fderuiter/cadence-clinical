using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Apps.Execution.Application;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// MIG-SUBJ-008 coverage: <see cref="SubjectResponse.FromAggregate"/> populates every field from the
/// aggregate's real current state (the legacy hard-coded SCREENING quirk was removed for #4771, D8). Also covers the shared snake_case wire-naming (`01_spec.md` §1.2) for all five
/// response records this ticket introduces.
/// </summary>
public class SubjectResponseTests
{
    private static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }

    private static ClinicalSubject CreateEnrolledSubjectWithSiteAndDemographics()
    {
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-EDGE-010"),
            new StudyCode("STUDY-001"),
            new SiteCode("SITE-042"),
            enrollmentIndex: 3,
            encryptedDemographics: "cipher-text",
            stratFactors: null);
        var transition = subject.TransitionTo(SubjectState.Enrolled);
        transition.IsSuccess.Should().BeTrue("Screening -> Enrolled must be a legal transition for this fixture setup to be valid");
        return subject;
    }

    // -- FromAggregate: full population from the aggregate's actual current state ---------------

    [Fact]
    public void FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState()
    {
        var subject = CreateEnrolledSubjectWithSiteAndDemographics();

        var response = SubjectResponse.FromAggregate(subject);

        response.Status.Should().Be("ENROLLED");
        response.SiteId.Should().Be(subject.SiteCode);
        response.Id.Should().Be(subject.Id);
        response.SubjectId_.Should().Be(subject.SubjectCode);
        response.StudyId.Should().Be(subject.StudyCode);
        response.TreatmentGroup.Should().Be(subject.TreatmentGroupCiphertext);
        response.ActiveProtocolVersion.Should().Be(subject.ActiveProtocolVersion);
        response.EncryptedDemographics.Should().Be(subject.EncryptedDemographics);
    }

    [Fact]
    public void FromAggregate_ScreeningSubject_ReportsScreeningStatus()
    {
        var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"),
            siteCode: null, enrollmentIndex: 0, encryptedDemographics: null, stratFactors: null);

        var response = SubjectResponse.FromAggregate(subject);

        response.Status.Should().Be("SCREENING");
        response.SiteId.Should().BeNull();
    }

    [Fact]
    public void FromAggregate_ForEnrolledSubjectWithSite_ReportsTheRealStatusAndSite()
    {
        // D8 (#4771): responses after a transition used to hard-code SCREENING. They now always map the
        // aggregate's actual state.
        var subject = CreateEnrolledSubjectWithSiteAndDemographics();

        var response = SubjectResponse.FromAggregate(subject);

        response.Status.Should().Be("ENROLLED");
        response.SiteId.Should().Be(subject.SiteCode);
        response.ActiveProtocolVersion.Should().Be(subject.ActiveProtocolVersion);
        response.EncryptedDemographics.Should().Be(subject.EncryptedDemographics);
    }

    // -- Wire shape: snake_case JSON property names (01_spec.md §1.2) ---------------------------

    [Fact]
    public void SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues()
    {
        var response = new SubjectResponse(
            new SubjectId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new SubjectCode("SUBJ-EDGE-010"), new StudyCode("STUDY-001"), SiteId: null, TreatmentGroup: null,
            Status: "SCREENING", ActiveProtocolVersion: "1.0.0", EncryptedDemographics: null);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
        var root = document.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("subject_id", out var subjectId).Should().BeTrue();
        root.TryGetProperty("study_id", out var studyId).Should().BeTrue();
        root.TryGetProperty("site_id", out var siteId).Should().BeTrue();
        root.TryGetProperty("treatment_group", out _).Should().BeTrue();
        root.TryGetProperty("status", out var status).Should().BeTrue();
        root.TryGetProperty("active_protocol_version", out _).Should().BeTrue();
        root.TryGetProperty("encrypted_demographics", out _).Should().BeTrue();

        root.TryGetProperty("subjectId", out _).Should().BeFalse("the wire format must use snake_case, not camelCase");

        // Plain scalar values, not the nested `{ "value": ... }` shape System.Text.Json would otherwise
        // produce for these readonly record struct wrapper types.
        subjectId.ValueKind.Should().Be(JsonValueKind.String);
        subjectId.GetString().Should().Be("SUBJ-EDGE-010");
        studyId.ValueKind.Should().Be(JsonValueKind.String);
        studyId.GetString().Should().Be("STUDY-001");
        siteId.ValueKind.Should().Be(JsonValueKind.Null);
        status.GetString().Should().Be("SCREENING");
    }

    [Fact]
    public void SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames()
    {
        var response = new SubjectDetailResponse(
            new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"), "ENROLLED",
            new SiteCode("SITE-001"), TreatmentGroup: "ARM-A", RandomizationSeed: "seed-1",
            InvestigationalProductId: "IP-1");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
        var root = document.RootElement;

        root.TryGetProperty("subject_id", out _).Should().BeTrue();
        root.TryGetProperty("study_id", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("site_id", out _).Should().BeTrue();
        root.TryGetProperty("treatment_group", out _).Should().BeTrue();
        root.TryGetProperty("randomization_seed", out _).Should().BeTrue();
        root.TryGetProperty("investigational_product_id", out _).Should().BeTrue();
    }

    [Fact]
    public void SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames()
    {
        var response = new SubjectConsentResponse(
            SubjectConsentId.New(), new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"),
            VersionTag: "v1", VersionIndex: 1, IcfSigned: true, IcfSignedDate: DateTimeOffset.UtcNow,
            RequiresReconsent: false, IsPaperOverride: false, Version: 1);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
        var root = document.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("subject_id", out _).Should().BeTrue();
        root.TryGetProperty("study_id", out _).Should().BeTrue();
        root.TryGetProperty("version_tag", out _).Should().BeTrue();
        root.TryGetProperty("version_index", out _).Should().BeTrue();
        root.TryGetProperty("icf_signed", out _).Should().BeTrue();
        root.TryGetProperty("icf_signed_date", out _).Should().BeTrue();
        root.TryGetProperty("requires_reconsent", out _).Should().BeTrue();
        root.TryGetProperty("is_paper_override", out _).Should().BeTrue();
        root.TryGetProperty("version", out _).Should().BeTrue();
    }

    [Fact]
    public void ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames()
    {
        var response = new ComplianceStatusResponse(new StudyCode("STUDY-001"), new SiteCode("SITE-001"),
            Milestone: "SITE_ACTIVATION", IsComplete: false, MissingDocuments: ["1572", "CV"]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
        var root = document.RootElement;

        root.TryGetProperty("study_id", out _).Should().BeTrue();
        root.TryGetProperty("site_id", out _).Should().BeTrue();
        root.TryGetProperty("milestone", out _).Should().BeTrue();
        root.TryGetProperty("is_complete", out _).Should().BeTrue();
        root.TryGetProperty("missing_documents", out var missingDocuments).Should().BeTrue();
        missingDocuments.ValueKind.Should().Be(JsonValueKind.Array);
        missingDocuments.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void OperationEnvelopeResponse_SerializesWithSpecFieldNames()
    {
        var response = new OperationEnvelopeResponse("success", "Site SITE-001 activated successfully.");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WireOptions));
        var root = document.RootElement;

        root.TryGetProperty("status", out var status).Should().BeTrue();
        root.TryGetProperty("message", out var message).Should().BeTrue();
        status.GetString().Should().Be("success");
        message.GetString().Should().Be("Site SITE-001 activated successfully.");
    }
}
