using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Round-trip, malformed-input, and casing/whitespace-normalization coverage for
/// `MIG-SUBJ-002`'s `SubjectStateCodec` (`normalize_state`'s `UPPER_SNAKE_CASE` port).
/// @req:PRD-SUB-001
/// </summary>
public class SubjectStateCodecTests
{
    [Theory]
    [InlineData(SubjectState.Screening, "SCREENING")]
    [InlineData(SubjectState.ScreenFailed, "SCREEN_FAILED")]
    [InlineData(SubjectState.Enrolled, "ENROLLED")]
    [InlineData(SubjectState.Randomized, "RANDOMIZED")]
    [InlineData(SubjectState.Active, "ACTIVE")]
    [InlineData(SubjectState.Unblinded, "UNBLINDED")]
    [InlineData(SubjectState.Completed, "COMPLETED")]
    [InlineData(SubjectState.Withdrawn, "WITHDRAWN")]
    [InlineData(SubjectState.ReconsentRequired, "RECONSENT_REQUIRED")]
    [GxPRequirement("PRD-SUB-001")]
    public void ToCode_RoundTripsThroughTryParse(SubjectState state, string expectedCode)
    {
        string code = SubjectStateCodec.ToCode(state);

        code.Should().Be(expectedCode);
        SubjectStateCodec.TryParse(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(state);
    }

    [Theory]
    [InlineData("screening", SubjectState.Screening)]
    [InlineData("  SCREENING  ", SubjectState.Screening)]
    [InlineData("screen failed", SubjectState.ScreenFailed)]
    [InlineData("Reconsent Required", SubjectState.ReconsentRequired)]
    [GxPRequirement("PRD-SUB-001")]
    public void TryParse_NormalizesCasingWhitespaceAndSpaces(string raw, SubjectState expected)
    {
        SubjectStateCodec.TryParse(raw, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("IN_PROGRESS")]
    [InlineData("in progress")]
    [InlineData("SCREENIN")]
    [GxPRequirement("PRD-SUB-001")]
    public void TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(string? raw)
    {
        bool result = SubjectStateCodec.TryParse(raw, out var state);

        result.Should().BeFalse();
        state.Should().Be(default(SubjectState));
    }
}
