using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Coverage for `MIG-SUBJ-003`'s `SubjectErrors` domain error factory: fixed `Code` strings,
/// `ErrorType` shape (including the two `with`-expression overrides), and message text.
/// </summary>
public class SubjectErrorsTests
{
    [Fact]
    public void NotFound_ReturnsFixedCodeAndType()
    {
        var error = SubjectErrors.NotFound("SUBJ-001");

        error.Code.Should().Be("SUBJECT_NOT_FOUND");
        error.Type.Should().Be(ErrorType.NotFound);
        error.Description.Should().Be("Subject not found");
    }

    [Fact]
    public void InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage()
    {
        var error = SubjectErrors.InvalidStateTransition("SCREENING", "RANDOMIZED");

        error.Code.Should().Be("INVALID_STATE_TRANSITION");
        error.Type.Should().Be(ErrorType.Validation);
        error.Description.Should().Be("Transition from SCREENING to RANDOMIZED is forbidden.");
        error.Description.Should().NotBe("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public void LockedFactorMutation_ReturnsFixedCodeAndConflictType()
    {
        var error = SubjectErrors.LockedFactorMutation();

        error.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType()
    {
        var error = SubjectErrors.SoftDeleteBlocked();

        error.Code.Should().Be("SOFT_DELETE_BLOCKED");
        error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public void BlockedEnrollment_ReturnsFixedCodeAndType()
    {
        var siteCode = new SiteCode("SITE-042");

        var error = SubjectErrors.BlockedEnrollment(siteCode);

        error.Code.Should().Be("BLOCKED_ENROLLMENT");
        error.Type.Should().Be(ErrorType.Validation);
        error.Description.Should().Contain("SITE-042");
    }

    [Fact]
    public void BlockedActivation_ReturnsFixedCodeAndType()
    {
        var error = SubjectErrors.BlockedActivation("SITE-042");

        error.Code.Should().Be("BLOCKED_ACTIVATION");
        error.Type.Should().Be(ErrorType.Validation);
        error.Description.Should().Contain("SITE-042");
    }

    [Fact]
    public void ReconsentRequired_ReturnsFixedCodeAndForbiddenType()
    {
        var error = SubjectErrors.ReconsentRequired("v2", "SUBJ-001", "VISIT-3");

        error.Code.Should().Be("RECONSENT_REQUIRED");
        error.Type.Should().Be(ErrorType.Forbidden);
        error.Description.Should().Contain("v2").And.Contain("SUBJ-001").And.Contain("VISIT-3");
    }

    [Fact]
    public void SignedConsentDateRequired_ReturnsFixedCodeAndValidationType()
    {
        var error = SubjectErrors.SignedConsentDateRequired();

        error.Code.Should().Be("ICF_SIGNED_DATE_REQUIRED");
        error.Type.Should().Be(ErrorType.Validation);
        error.Description.Should().Contain("icf_signed_date");
    }

    [Fact]
    public void AllFactoryMethods_ProduceDistinctCodes()
    {
        var codes = new[]
        {
            SubjectErrors.NotFound("SUBJ-001").Code,
            SubjectErrors.InvalidStateTransition("SCREENING", "RANDOMIZED").Code,
            SubjectErrors.LockedFactorMutation().Code,
            SubjectErrors.SoftDeleteBlocked().Code,
            SubjectErrors.BlockedEnrollment(null).Code,
            SubjectErrors.BlockedActivation("SITE-042").Code,
            SubjectErrors.ReconsentRequired("v2", "SUBJ-001", "VISIT-3").Code,
            SubjectErrors.SignedConsentDateRequired().Code,
            SubjectErrors.UnauthorizedParticipantRole("cra").Code,
            SubjectErrors.UnauthorizedUnblindRole("crc").Code,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().HaveCount(10);
    }
}
