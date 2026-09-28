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
/// MIG-SUBJ-008 coverage: <see cref="SubjectResponse.FromAggregate"/>'s full population versus
/// <see cref="SubjectResponse.Underpopulated"/>'s deliberate §2.6 quirk — byte-for-byte legacy parity
/// with `fixtures/output_expected_nominal.json`'s <c>EDGE-010</c> and <c>SCN-003-A</c> family. The
/// hardcoded-default behavior asserted below is a tracked parity decision, not a bug to "fix"; full
/// HTTP-level fixture replay is MIG-SUBJ-026's concern, this suite only exercises the DTO construction
/// logic in isolation. Also covers the shared snake_case wire-naming (`01_spec.md` §1.2) for all five
/// response records this ticket introduces.
/// @req:PRD-SUB-001
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
    [GxPRequirement("PRD-SUB-001")]
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
    [GxPRequirement("PRD-SUB-001")]
    public void FromAggregate_ScreeningSubject_ReportsScreeningStatus()
    {
        var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"),
            siteCode: null, enrollmentIndex: 0, encryptedDemographics: null, stratFactors: null);

        var response = SubjectResponse.FromAggregate(subject);

        response.Status.Should().Be("SCREENING");
        response.SiteId.Should().BeNull();
    }

    // -- Underpopulated: the deliberate §2.6 quirk -----------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState()
    {
        // This is the documented quirk (02_technical_design.md §2.6 / 01_spec.md §2.2 Rule 9), verified
        // byte-for-byte against fixtures/output_expected_nominal.json's EDGE-010 and SCN-003-A: the
        // subject really is ENROLLED with a real site, but Underpopulated is a pure function of
        // (id, subjectCode, studyCode, encryptedDemographics) alone — it structurally cannot see, and
        // must not reflect, the aggregate's actual Status/SiteCode/TreatmentGroup. Do not "fix" this
        // assertion to expect ENROLLED — that would misrepresent the legacy behavior this port must
        // reproduce.
        var subject = CreateEnrolledSubjectWithSiteAndDemographics();

        var response = SubjectResponse.Underpopulated(
            subject.Id, subject.SubjectCode, subject.StudyCode, subject.EncryptedDemographics);

        response.Status.Should().Be("SCREENING");
        response.ActiveProtocolVersion.Should().Be("1.0.0");
        response.SiteId.Should().BeNull("Underpopulated always nulls SiteId even though this subject's real SiteCode is non-null");
        response.TreatmentGroup.Should().BeNull();
        response.Id.Should().Be(subject.Id);
        response.SubjectId_.Should().Be(subject.SubjectCode);
        response.StudyId.Should().Be(subject.StudyCode);
        response.EncryptedDemographics.Should().Be(subject.EncryptedDemographics);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate()
    {
        // The core §2.6 divergence the golden-master harness (MIG-SUBJ-026) grades against: the same
        // ENROLLED aggregate produces two different response bodies depending purely on which factory
        // the caller invokes, exactly mirroring EDGE-010's db_assertions.status ("ENROLLED") versus
        // response body.status ("SCREENING") split.
        var subject = CreateEnrolledSubjectWithSiteAndDemographics();

        var full = SubjectResponse.FromAggregate(subject);
        var underpopulated = SubjectResponse.Underpopulated(
            subject.Id, subject.SubjectCode, subject.StudyCode, subject.EncryptedDemographics);

        full.Status.Should().Be("ENROLLED");
        underpopulated.Status.Should().Be("SCREENING");
        full.Status.Should().NotBe(underpopulated.Status);

        // ActiveProtocolVersion is the fixture's other named divergent field (EDGE-010: DB row "2.1.0"
        // vs. response body "1.0.0"). ClinicalSubject (MIG-SUBJ-004) does not yet expose any public way
        // to move a subject off its "1.0.0" construction default — that capability belongs to a later,
        // out-of-slice ticket — so both factories agree on this value for every aggregate reachable
        // through today's public API. Underpopulated's independence from the aggregate's real
        // ActiveProtocolVersion is instead proven structurally (see the theory below): its signature
        // never accepts a ClinicalSubject, or an ActiveProtocolVersion, as input at all, so it cannot
        // read one no matter what a future caller's aggregate state is.
        full.ActiveProtocolVersion.Should().Be(subject.ActiveProtocolVersion);
        underpopulated.ActiveProtocolVersion.Should().Be("1.0.0");
    }

    public static IEnumerable<object?[]> UnderpopulatedInputVariations()
    {
        yield return new object?[] { new SubjectCode("SUBJ-A"), new StudyCode("STUDY-A"), "cipher-a" };
        yield return new object?[] { new SubjectCode("SUBJ-B"), new StudyCode("STUDY-B"), null };
    }

    [Theory]
    [MemberData(nameof(UnderpopulatedInputVariations))]
    [GxPRequirement("PRD-SUB-001")]
    public void Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs(
        SubjectCode subjectCode, StudyCode studyCode, string? encryptedDemographics)
    {
        var response = SubjectResponse.Underpopulated(SubjectId.New(), subjectCode, studyCode, encryptedDemographics);

        response.Status.Should().Be("SCREENING");
        response.ActiveProtocolVersion.Should().Be("1.0.0");
        response.SiteId.Should().BeNull();
        response.TreatmentGroup.Should().BeNull();
    }

    // -- Wire shape: snake_case JSON property names (01_spec.md §1.2) ---------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues()
    {
        var response = SubjectResponse.Underpopulated(
            new SubjectId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new SubjectCode("SUBJ-EDGE-010"), new StudyCode("STUDY-001"), encryptedDemographics: null);

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
    [GxPRequirement("PRD-SUB-001")]
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
    [GxPRequirement("PRD-SUB-001")]
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
    [GxPRequirement("PRD-SUB-001")]
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
    [GxPRequirement("PRD-SUB-001")]
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
