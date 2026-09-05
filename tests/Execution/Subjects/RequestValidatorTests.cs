using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Validator coverage for `MIG-SUBJ-009`'s six request DTOs: `SubjectCreateRequest`,
/// `SubjectConsentRequest`, `SubjectStateUpdateRequest`, `SubjectDemographicsUpdateRequest`,
/// `EtmfCompletenessWebhookPayload`, and `SiteActivationRequest`. Includes explicit coverage of the two
/// truthiness-sensitive fields `01_spec.md` §3 names directly: `SubjectStateUpdateRequest`'s
/// `status`/`state` alternative-name pair, and the `SiteId` empty-string trap. Full fixture-replay
/// verification against `input_invalid_validation_errors.json` happens in `MIG-SUBJ-026`; this file only
/// unit-tests the DTO/validator shapes in isolation.
/// @req:PRD-SUB-001
/// </summary>
public class RequestValidatorTests
{
    // ---- SubjectCreateRequestValidator ------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_FullyPopulatedRequest_Passes()
    {
        var request = new SubjectCreateRequest(
            "SUBJ-001", "STUDY-001", "SITE-001", "ARM-A",
            new DemographicsPayload("Jane Doe", "1990-01-01", "F", "White"));

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SITE-001")]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(string? siteId)
    {
        // SiteId is legitimately optional (01_spec.md §3 truthiness-trap table): neither "absent" (null)
        // nor "explicitly empty" ("") fails this DTO's own shape rules — the empty-string-as-absent
        // ambiguity is resolved downstream by SiteCode's constructor (MIG-SUBJ-001), not suppressed here.
        var request = new SubjectCreateRequest("SUBJ-001", "STUDY-001", siteId, null, null);

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength()
    {
        var request = new SubjectCreateRequest("SUBJ-001", "STUDY-001", new string('a', 256), null, null);

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SiteId");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty()
    {
        var request = new SubjectCreateRequest("", "STUDY-001", null, null, null);

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SubjectId");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty()
    {
        var request = new SubjectCreateRequest("SUBJ-001", "", null, null, null);

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "StudyId");
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity(
        int length, bool expectedValid)
    {
        var value = new string('a', length);
        var request = new SubjectCreateRequest(value, value, null, null, null);

        var result = new SubjectCreateRequestValidator().Validate(request);

        result.IsValid.Should().Be(expectedValid);
    }

    // ---- SubjectConsentRequestValidator ------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectConsentRequestValidator_ValidRequest_Passes()
    {
        var request = new SubjectConsentRequest(
            new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true,
            IcfSignedDate: DateTimeOffset.UtcNow);

        var result = new SubjectConsentRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing()
    {
        // null! exercises the NotNull() guard against a caller that bypasses compile-time nullability
        // (e.g. a JSON payload with a missing/null "protocolVersion" field deserializing into this
        // non-nullable constructor parameter). The nested StudyId/VersionTag rules are each guarded by
        // `.When(x => x.ProtocolVersion is not null)`, so this must fail with exactly one error rather
        // than throwing a NullReferenceException while evaluating them.
        var request = new SubjectConsentRequest(null!, IcfSigned: true, IcfSignedDate: null);

        var act = () => new SubjectConsentRequestValidator().Validate(request);

        act.Should().NotThrow();
        var result = new SubjectConsentRequestValidator().Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].PropertyName.Should().Be("ProtocolVersion");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError()
    {
        var request = new SubjectConsentRequest(
            new ProtocolVersionPayload("", "2.0", 2), IcfSigned: true, IcfSignedDate: null);

        var result = new SubjectConsentRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError()
    {
        var request = new SubjectConsentRequest(
            new ProtocolVersionPayload("STUDY-001", "", 2), IcfSigned: true, IcfSignedDate: null);

        var result = new SubjectConsentRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    // ---- SubjectStateUpdateRequest / SubjectStateUpdateRequestValidator ----------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode()
    {
        var request = new SubjectStateUpdateRequest(null, null);

        var result = new SubjectStateUpdateRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("MISSING_TARGET_STATE");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData(null, "   ")]
    [InlineData("  ", "  ")]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(
        string? status, string? state)
    {
        // Reproduces the fix for the `if not target_state:` truthiness trap (01_spec.md §3): an empty or
        // whitespace-only string must be treated identically to an omitted field on *both* sides of the
        // status/state pair, not just the already-mitigated site_id case.
        var request = new SubjectStateUpdateRequest(status, state);

        var result = new SubjectStateUpdateRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("MISSING_TARGET_STATE");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState()
    {
        var request = new SubjectStateUpdateRequest(null, "ENROLLED");

        var result = new SubjectStateUpdateRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
        request.Target.Should().Be("ENROLLED");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus()
    {
        var request = new SubjectStateUpdateRequest("ENROLLED", null);

        var result = new SubjectStateUpdateRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
        request.Target.Should().Be("ENROLLED");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus()
    {
        // Documents a deliberate asymmetry between the validator (per 02_technical_design.md §2.7, checks
        // Status/State independently with IsNullOrWhiteSpace, so a populated State alone is sufficient)
        // and Target (a plain `??` null-coalesce, which only treats *null* as "not provided"). A caller
        // that sends an explicitly-empty "status" alongside a populated "state" passes validation here,
        // but Target still resolves to the empty Status rather than falling through to State. This is the
        // exact DTO shape mandated verbatim by the ticket/design doc, not a defect introduced in this
        // implementation; downstream state-string parsing (SubjectStateCodec.TryParse) rejects the empty
        // Target as an unmapped state rather than silently succeeding.
        var request = new SubjectStateUpdateRequest("", "ENROLLED");

        var result = new SubjectStateUpdateRequestValidator().Validate(request);

        result.IsValid.Should().BeTrue();
        request.Target.Should().Be("");
    }

    // ---- EtmfCompletenessWebhookPayloadValidator ---------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes()
    {
        var payload = new EtmfCompletenessWebhookPayload(
            "STUDY-001", "SITE-001", "SITE_ACTIVATION", true, ["Form 1572"]);

        var result = new EtmfCompletenessWebhookPayloadValidator().Validate(payload);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes()
    {
        var payload = new EtmfCompletenessWebhookPayload(
            "STUDY-001", null, "SITE_ACTIVATION", false, null);

        var result = new EtmfCompletenessWebhookPayloadValidator().Validate(payload);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [GxPRequirement("PRD-SUB-001")]
    public void EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty(string studyId)
    {
        // FluentValidation's NotEmpty() treats whitespace-only strings as empty, so both "" and "   "
        // must fail identically.
        var payload = new EtmfCompletenessWebhookPayload(studyId, null, "SITE_ACTIVATION", true, null);

        var result = new EtmfCompletenessWebhookPayloadValidator().Validate(payload);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "StudyId");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty()
    {
        var payload = new EtmfCompletenessWebhookPayload("STUDY-001", null, "", true, null);

        var result = new EtmfCompletenessWebhookPayloadValidator().Validate(payload);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Milestone");
    }

    // ---- SiteActivationRequest / SubjectDemographicsUpdateRequest (no dedicated validator) ---------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SiteActivationRequest_ValueEquality_ComparesByStudyId()
    {
        // No dedicated validator (02_technical_design.md §2.7): StudyId is its only field, and ASP.NET
        // Core model binding rejects a missing required non-nullable string before any validator runs.
        var first = new SiteActivationRequest("STUDY-GATED");
        var second = new SiteActivationRequest("STUDY-GATED");

        first.Should().Be(second);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls()
    {
        var request = new SubjectDemographicsUpdateRequest(null, null);

        request.Demographics.Should().BeNull();
        request.StratFactors.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips()
    {
        var stratFactors = new Dictionary<string, object?> { ["site"] = "A", ["age_band"] = null };

        var request = new SubjectDemographicsUpdateRequest(null, stratFactors);

        request.StratFactors.Should().BeEquivalentTo(stratFactors);
    }
}
