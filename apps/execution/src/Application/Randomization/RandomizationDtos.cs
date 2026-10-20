using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Randomization;

/// <summary>
/// <c>POST /api/v1/execution/randomization/schemes</c>: drafts the study's next scheme version (#5607). Method is
/// <c>permuted_block</c> (a list generated when the version is approved) or <c>minimization</c> (dynamic).
/// @req:PRD-SUB-001
/// </summary>
public sealed record RandomizationSchemeDraftRequest(
    StudyCode StudyId,
    string Method,
    Dictionary<string, int> Arms,
    Dictionary<string, List<string>>? StratificationFactors,
    List<int>? BlockSizes,
    int? SlotsPerStratum,
    double? PreferredArmProbability,
    bool RequiresKit);

/// <summary>Shape checks for <see cref="RandomizationSchemeDraftRequest"/>; the scheme's own rules are checked when it is drafted.</summary>
public sealed class RandomizationSchemeDraftRequestValidator : AbstractValidator<RandomizationSchemeDraftRequest>
{
    public RandomizationSchemeDraftRequestValidator()
    {
        RuleFor(x => x.StudyId.Value).NotEmpty().WithName("study_id");
        RuleFor(x => x.Method).Must(m => RandomizationMethodCodec.TryParse(m, out _))
            .WithMessage("method must be 'permuted_block' or 'minimization'.");
        RuleFor(x => x.Arms).NotNull();
    }
}

/// <summary>
/// <c>POST /api/v1/execution/subjects/{subjectId}/randomize</c>: the participant's stratification factor values, one
/// per factor the live scheme declares (#5607).
/// @req:PRD-SUB-001
/// </summary>
public sealed record RandomizeSubjectRequest(Dictionary<string, string>? StratificationFactors);

/// <summary>
/// A scheme version as anyone with scheme access may see it: its configuration and approval, never its list.
/// @req:PRD-SUB-001
/// </summary>
public sealed record RandomizationSchemeResponse(
    Guid Id,
    StudyCode StudyId,
    int Version,
    string Method,
    string Status,
    IReadOnlyDictionary<string, int> Arms,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StratificationFactors,
    IReadOnlyList<int> BlockSizes,
    int SlotsPerStratum,
    double PreferredArmProbability,
    bool RequiresKit,
    int ListSize,
    string ConfigurationHash,
    string DraftedBy,
    DateTimeOffset DraftedAt,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RetiredAt)
{
    public static RandomizationSchemeResponse From(RandomizationScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        return new(scheme.Id, scheme.StudyCode, scheme.Version, RandomizationMethodCodec.ToCode(scheme.Method),
            scheme.Status.ToString().ToUpperInvariant(), scheme.Arms(), scheme.StratificationFactors(), scheme.BlockSizes(),
            scheme.SlotsPerStratum, scheme.PreferredArmProbability, scheme.RequiresKit, scheme.ListSize, scheme.ConfigurationHash,
            scheme.DraftedBy, scheme.DraftedAt, scheme.ApprovedBy, scheme.ApprovedAt, scheme.RetiredAt);
    }
}

/// <summary>
/// The result of randomising a participant: which allocation and kit, never which arm, so it is safe for the blinded
/// site staff who randomise (#5607).
/// @req:PRD-SUB-001
/// </summary>
public sealed record RandomizationResponse(
    Guid RandomizationId,
    SubjectCode SubjectId,
    StudyCode StudyId,
    int SchemeVersion,
    string StratumKey,
    string? KitNumber,
    string Status,
    DateTimeOffset RandomizedAt);

/// <summary>A participant's arm and kit, revealed only to an unblinded caller.</summary>
public sealed record RevealedAllocation(string Arm, string? KitNumber);

/// <summary>The wire codes of <see cref="RandomizationMethod"/>.</summary>
public static class RandomizationMethodCodec
{
    public static string ToCode(RandomizationMethod method) => method switch
    {
        RandomizationMethod.PermutedBlock => "permuted_block",
        RandomizationMethod.Minimization => "minimization",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public static bool TryParse(string? code, out RandomizationMethod method)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "permuted_block":
                method = RandomizationMethod.PermutedBlock;
                return true;
            case "minimization":
            case "minimisation":
                method = RandomizationMethod.Minimization;
                return true;
            default:
                method = default;
                return false;
        }
    }
}
