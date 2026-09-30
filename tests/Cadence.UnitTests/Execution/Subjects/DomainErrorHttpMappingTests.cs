using System;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Coverage for `MIG-SUBJ-010`'s <see cref="DomainError"/> -&gt; RFC 9457 <c>ProblemDetails</c> HTTP mapping:
/// one <c>Theory</c> case per <see cref="SubjectErrors"/> factory method asserting the exact resulting
/// HTTP status via <c>DomainError.ToProblemResult()</c>, explicit highlight coverage of the two named-`Code`
/// overrides (422, 423), the "keyed by Code, not Type" boundary check, and the `Program.cs`
/// `CustomizeProblemDetails` safety net's <see cref="DomainErrorHttpMapping.ApplyKnownCodeStatus"/> — proving
/// it forces the one family the legacy system lacked a global handler for (`SUBJECT_NOT_FOUND`) to 404, while
/// leaving anything it does not recognize (most importantly a bare exception's generic 500) untouched, so
/// raise site #19's blanket downgrade-to-400 bug is never reproduced.
/// </summary>
public class DomainErrorHttpMappingTests
{
    [Theory]
    [InlineData("NotFound", StatusCodes.Status404NotFound)]
    [InlineData("InvalidStateTransition", StatusCodes.Status400BadRequest)]
    [InlineData("LockedFactorMutation", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("SoftDeleteBlocked", StatusCodes.Status403Forbidden)]
    [InlineData("BlockedEnrollment", StatusCodes.Status400BadRequest)]
    [InlineData("BlockedActivation", StatusCodes.Status400BadRequest)]
    [InlineData("ReconsentRequired", StatusCodes.Status423Locked)]
    [InlineData("SignedConsentDateRequired", StatusCodes.Status400BadRequest)]
    public void ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(string factory, int expectedStatus)
    {
        var error = CreateSubjectError(factory);

        var result = error.ToProblemResult();

        result.StatusCode.Should().Be(expectedStatus);
        result.ProblemDetails.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409()
    {
        var error = SubjectErrors.LockedFactorMutation();
        error.Type.Should().Be(ErrorType.Conflict); // sanity: the override genuinely deviates from Type's own default

        var result = error.ToProblemResult();

        result.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("LOCKED_FACTOR_MUTATION");
        result.ProblemDetails.Detail.Should().Be(error.Description);
    }

    [Fact]
    public void ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403()
    {
        var error = SubjectErrors.ReconsentRequired("v2", "SUBJ-001", "VISIT-3");
        error.Type.Should().Be(ErrorType.Forbidden); // sanity: the override genuinely deviates from Type's own default

        var result = error.ToProblemResult();

        result.StatusCode.Should().Be(StatusCodes.Status423Locked);
        result.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("RECONSENT_REQUIRED");
        result.ProblemDetails.Detail.Should().Be(error.Description);
    }

    [Fact]
    public void ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded()
    {
        // SOFT_DELETE_BLOCKED shares `Type = Forbidden` with RECONSENT_REQUIRED but, unlike it, needs no
        // override: this confirms the override table is scoped to the one named code, not the whole Type.
        var error = SubjectErrors.SoftDeleteBlocked();

        var result = error.ToProblemResult();

        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409()
    {
        // Boundary check: the override table is keyed by Code, not Type. A made-up Conflict-typed error
        // that isn't LOCKED_FACTOR_MUTATION must still fall through to Conflict's own default (409), proving
        // the 422 override is scoped to the one named code rather than leaking onto every Conflict-typed error.
        var hypotheticalConflictError = DomainError.Conflict("SOME_OTHER_CONFLICT_CODE", "Unrelated conflict.");

        var statusCode = DomainErrorHttpMapping.GetStatusCode(hypotheticalConflictError);

        statusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged()
    {
        // Simulates the ProblemDetails ASP.NET Core auto-generates for an exception that was never wrapped
        // in a DomainError (raise site #19's bare catch-all must not be reproduced): with no `code`
        // extension at all, the global CustomizeProblemDetails safety net (Program.cs) must leave the
        // generic 500 exactly as the exception-handler middleware set it -- never downgrading it to 400.
        var problemDetails = new ProblemDetails { Status = StatusCodes.Status500InternalServerError };

        DomainErrorHttpMapping.ApplyKnownCodeStatus(problemDetails);

        problemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged()
    {
        var problemDetails = new ProblemDetails { Status = StatusCodes.Status500InternalServerError };
        problemDetails.Extensions["code"] = "SOME_UNRELATED_CODE";

        DomainErrorHttpMapping.ApplyKnownCodeStatus(problemDetails);

        problemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404()
    {
        // The one family `01_spec.md` §5 flags as never having had a global handler in the legacy system --
        // proves the defense-in-depth safety net actually recognizes and corrects it, even from a bare
        // `code` string with no DomainError.Type available.
        var problemDetails = new ProblemDetails { Status = StatusCodes.Status500InternalServerError };
        problemDetails.Extensions["code"] = "SUBJECT_NOT_FOUND";

        DomainErrorHttpMapping.ApplyKnownCodeStatus(problemDetails);

        problemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    private static DomainError CreateSubjectError(string factory) => factory switch
    {
        "NotFound" => SubjectErrors.NotFound("SUBJ-001"),
        "InvalidStateTransition" => SubjectErrors.InvalidStateTransition("SCREENING", "RANDOMIZED"),
        "LockedFactorMutation" => SubjectErrors.LockedFactorMutation(),
        "SoftDeleteBlocked" => SubjectErrors.SoftDeleteBlocked(),
        "BlockedEnrollment" => SubjectErrors.BlockedEnrollment(new SiteCode("SITE-042")),
        "BlockedActivation" => SubjectErrors.BlockedActivation("SITE-042"),
        "ReconsentRequired" => SubjectErrors.ReconsentRequired("v2", "SUBJ-001", "VISIT-3"),
        "SignedConsentDateRequired" => SubjectErrors.SignedConsentDateRequired(),
        _ => throw new ArgumentOutOfRangeException(nameof(factory), factory, "Unknown SubjectErrors factory key."),
    };
}
