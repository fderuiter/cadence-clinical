using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Randomization;

/// <summary>
/// The randomisation scheme version (#5607, ADR-2640): what a draft must declare before it can go live, the
/// second-person approval, and the canonical strata a participant's factor values map to.
/// </summary>
public sealed class RandomizationSchemeTests
{
    private static readonly StudyCode Study = new("STUDY-RND");
    private static readonly FakeClock Clock = new();

    private static readonly Dictionary<string, int> TwoArms = new() { ["PLACEBO"] = 1, ["ACTIVE"] = 1 };

    private static readonly Dictionary<string, IReadOnlyList<string>> SexAndAge = new()
    {
        ["sex"] = ["F", "M"],
        ["age_group"] = ["<50", ">=50"],
    };

    private static Result<RandomizationScheme> DraftBlock(
        IReadOnlyDictionary<string, int>? arms = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? factors = null,
        IReadOnlyList<int>? blockSizes = null,
        int slotsPerStratum = 8) =>
        RandomizationScheme.Draft(Study, 1, RandomizationMethod.PermutedBlock, arms ?? TwoArms, factors, blockSizes,
            slotsPerStratum, preferredArmProbability: 0.8, requiresKit: false, "statistician-a", "Initial list", Clock);

    [Fact]
    [GxPRequirement("PRD-SUB-003")]
    public void Draft_PermutedBlock_StratifiesByEveryFactorLevelCombination()
    {
        var scheme = DraftBlock(factors: SexAndAge, blockSizes: [4, 2]).Value!;

        scheme.Status.Should().Be(RandomizationSchemeStatus.Draft);
        scheme.BlockSizes().Should().Equal(2, 4);
        scheme.Strata().Should().Equal(
            "age_group=<50|sex=F", "age_group=<50|sex=M", "age_group=>=50|sex=F", "age_group=>=50|sex=M");
        scheme.ListSize.Should().Be(4 * 8);
    }

    [Fact]
    public void Draft_WithoutFactors_HasTheSingleDefaultStratum()
    {
        var scheme = DraftBlock().Value!;

        scheme.Strata().Should().Equal(RandomizationScheme.DefaultStratum);
        scheme.StratumFor(null).Value.Should().Be(RandomizationScheme.DefaultStratum);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Draft_RefusesABlockSizeThatIsNotAMultipleOfTheRatioSum(int blockSize)
    {
        var result = DraftBlock(blockSizes: [blockSize]);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Draft_RefusesASingleArm()
    {
        var result = DraftBlock(arms: new Dictionary<string, int> { ["ACTIVE"] = 1 });

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Draft_RefusesAListLargerThanTheLimit()
    {
        var result = DraftBlock(factors: SexAndAge, slotsPerStratum: RandomizationScheme.MaxListSize);

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.2)]
    [GxPRequirement("PRD-SUB-004")]
    public void Draft_Minimization_NeedsFactorsAndAPreferredArmProbabilityAboveOneHalf(double probability)
    {
        var withoutFactors = RandomizationScheme.Draft(Study, 1, RandomizationMethod.Minimization, TwoArms, null, null,
            0, 0.8, false, "statistician-a", "Initial", Clock);
        var badProbability = RandomizationScheme.Draft(Study, 1, RandomizationMethod.Minimization, TwoArms, SexAndAge, null,
            0, probability, false, "statistician-a", "Initial", Clock);

        withoutFactors.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
        badProbability.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Approve_ByTheDrafter_IsRefused_AndTheVersionStaysDraft()
    {
        var scheme = DraftBlock().Value!;

        var result = scheme.Approve("STATISTICIAN-A", "Go live", Clock);

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_SELF_APPROVAL");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        scheme.Status.Should().Be(RandomizationSchemeStatus.Draft);
    }

    [Fact]
    public void Approve_ByASecondPerson_MakesItLive_AndItCannotBeApprovedTwice()
    {
        var scheme = DraftBlock().Value!;

        scheme.Approve("statistician-b", "Go live", Clock).IsSuccess.Should().BeTrue();

        scheme.Status.Should().Be(RandomizationSchemeStatus.Approved);
        scheme.ApprovedBy.Should().Be("statistician-b");
        scheme.Approve("statistician-c", "Again", Clock).Error!.Code.Should().Be("RANDOMIZATION_SCHEME_NOT_DRAFT");
    }

    [Fact]
    public void Retire_EndsAnApprovedVersion()
    {
        var scheme = DraftBlock().Value!;
        scheme.Approve("statistician-b", "Go live", Clock);

        scheme.Retire("Superseded by version 2", Clock);

        scheme.Status.Should().Be(RandomizationSchemeStatus.Retired);
        scheme.RetiredAt.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public void StratumFor_IsCanonical_WhateverOrderAndSpacingTheValuesArriveIn()
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;

        var stratum = scheme.StratumFor(new Dictionary<string, string> { ["sex"] = " M ", ["age_group"] = ">=50" });

        stratum.Value.Should().Be("age_group=>=50|sex=M");
    }

    [Theory]
    [InlineData("sex", "X", "STRATIFICATION_LEVEL_UNKNOWN")]
    [InlineData("site", "S1", "STRATIFICATION_FACTOR_UNKNOWN")]
    public void StratumFor_RefusesUndeclaredValues(string factor, string level, string code)
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;
        var values = new Dictionary<string, string> { ["sex"] = "F", ["age_group"] = "<50", [factor] = level };

        scheme.StratumFor(values).Error!.Code.Should().Be(code);
    }

    [Fact]
    public void StratumFor_RefusesAMissingFactor()
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;

        scheme.StratumFor(new Dictionary<string, string> { ["sex"] = "F" }).Error!.Code
            .Should().Be("STRATIFICATION_FACTOR_MISSING");
    }

    [Fact]
    public void ListSlot_IsConsumedOnce()
    {
        var slot = RandomizationListSlot.Create(Guid.NewGuid(), RandomizationScheme.DefaultStratum, 1, "enc:v1:k:x");
        slot.Consume(Guid.NewGuid());

        var again = () => slot.Consume(Guid.NewGuid());

        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Subject_IsRandomisedOnlyFromEnrolled_AndOnlyOnce()
    {
        var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-RND-1"), Study, null, 1, null, null);

        subject.Randomize(Guid.NewGuid(), null, new Dictionary<string, object?>()).IsFailure
            .Should().BeTrue("a screening participant cannot skip enrolment");
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
        subject.Randomize(Guid.NewGuid(), "KIT-1", new Dictionary<string, object?> { ["sex"] = "F" }).IsSuccess.Should().BeTrue();

        subject.Status.Should().Be(SubjectState.Randomized);
        subject.KitReference.Should().Be("KIT-1");
        subject.Randomize(Guid.NewGuid(), null, new Dictionary<string, object?>()).Error!.Code
            .Should().Be("SUBJECT_ALREADY_RANDOMIZED");
    }
}
