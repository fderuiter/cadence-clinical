using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Domain.Randomization;

/// <summary>How a <see cref="RandomizationScheme"/> allocates participants to arms (ICH E9 §2.3).</summary>
public enum RandomizationMethod
{
    /// <summary>A pre-generated list of permuted blocks per stratum, consumed in order.</summary>
    PermutedBlock,

    /// <summary>Dynamic allocation by Pocock-Simon minimisation over the stratification factors.</summary>
    Minimization,
}

/// <summary>The lifecycle of a <see cref="RandomizationScheme"/> version.</summary>
public enum RandomizationSchemeStatus
{
    /// <summary>Written but not signed off; it allocates nobody.</summary>
    Draft,

    /// <summary>Signed off by a second person; the study's one live scheme.</summary>
    Approved,

    /// <summary>Replaced by a later approved version; it allocates nobody.</summary>
    Retired,
}

/// <summary>
/// One version of a study's randomisation scheme: the arms and their ratio, the stratification factors and their
/// levels, and the method (#5607). A scheme is immutable once drafted, so a change is a new version. It allocates
/// nobody until a second person approves it (go-live sign-off, ICH E6(R3) 4.2 and Annex 1 on randomisation), and
/// approving a version retires the one before it. The scheme holds no allocation and no seed: a permuted-block list is
/// generated when the version is approved and stored only encrypted (<see cref="RandomizationListSlot"/>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class RandomizationScheme : AggregateRoot<Guid>
{
    /// <summary>The stratum key of a scheme without stratification factors.</summary>
    public const string DefaultStratum = "DEFAULT";

    /// <summary>The largest list a permuted-block scheme may generate across all its strata.</summary>
    public const int MaxListSize = 100_000;

    public StudyCode StudyCode { get; private set; } = default!;
    public int Version { get; private set; }
    public RandomizationMethod Method { get; private set; }
    public RandomizationSchemeStatus Status { get; private set; }

    /// <summary>Arm code to its allocation ratio, as canonical JSON.</summary>
    public string ArmsJson { get; private set; } = "{}";

    /// <summary>Stratification factor to its allowed levels, as canonical JSON.</summary>
    public string StratificationFactorsJson { get; private set; } = "{}";

    /// <summary>Allowed permuted-block sizes, as JSON (permuted-block schemes only).</summary>
    public string BlockSizesJson { get; private set; } = "[]";

    /// <summary>Slots generated per stratum when the version is approved (permuted-block schemes only).</summary>
    public int SlotsPerStratum { get; private set; }

    /// <summary>The probability of assigning the arm that minimises imbalance (minimisation schemes only).</summary>
    public double PreferredArmProbability { get; private set; }

    /// <summary>Whether randomising a participant also assigns a kit of the allocated arm at their site.</summary>
    public bool RequiresKit { get; private set; }

    /// <summary>SHA-256 of the canonical configuration, fixed when drafted and named by the approval.</summary>
    public string ConfigurationHash { get; private set; } = string.Empty;

    public string DraftedBy { get; private set; } = string.Empty;
    public DateTimeOffset DraftedAt { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovalReason { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Number of encrypted list slots generated on approval (permuted-block schemes only).</summary>
    public int ListSize { get; private set; }

    private RandomizationScheme() { } // EF Core

    /// <summary>Arm code to ratio.</summary>
    public IReadOnlyDictionary<string, int> Arms() =>
        new SortedDictionary<string, int>(
            JsonSerializer.Deserialize<Dictionary<string, int>>(ArmsJson) ?? [], StringComparer.Ordinal);

    /// <summary>Stratification factor to its allowed levels.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StratificationFactors() =>
        (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(StratificationFactorsJson) ?? [])
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);

    /// <summary>Allowed block sizes.</summary>
    public IReadOnlyList<int> BlockSizes() => JsonSerializer.Deserialize<List<int>>(BlockSizesJson) ?? [];

    /// <summary>
    /// Drafts a new scheme version. Every rule a live scheme depends on is checked here, so an approved version can
    /// always allocate: at least two arms with positive ratios, levels for every factor, block sizes that are positive
    /// multiples of the ratio sum, and a list that fits <see cref="MaxListSize"/>.
    /// </summary>
    public static Result<RandomizationScheme> Draft(
        StudyCode studyCode,
        int version,
        RandomizationMethod method,
        IReadOnlyDictionary<string, int> arms,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? stratificationFactors,
        IReadOnlyList<int>? blockSizes,
        int slotsPerStratum,
        double preferredArmProbability,
        bool requiresKit,
        string draftedBy,
        string reasonForChange,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(arms);
        ArgumentNullException.ThrowIfNull(clock);

        var normalizedArms = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, ratio) in arms)
        {
            string trimmed = code?.Trim() ?? string.Empty;
            if (trimmed.Length is 0 or > 64)
                return RandomizationErrors.InvalidScheme("Every arm needs a code of 1 to 64 characters.");
            if (ratio < 1)
                return RandomizationErrors.InvalidScheme($"Arm '{trimmed}' needs a ratio of at least 1.");
            if (!normalizedArms.TryAdd(trimmed, ratio))
                return RandomizationErrors.InvalidScheme($"Arm '{trimmed}' is listed twice.");
        }

        if (normalizedArms.Count < 2)
            return RandomizationErrors.InvalidScheme("A randomisation scheme needs at least two arms.");

        var normalizedFactors = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (factor, levels) in stratificationFactors ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            string name = factor?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 64 || name.Contains('=', StringComparison.Ordinal) || name.Contains('|', StringComparison.Ordinal))
                return RandomizationErrors.InvalidScheme("Every stratification factor needs a name of 1 to 64 characters without '=' or '|'.");
            var distinct = (levels ?? []).Select(l => l?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count == 0 || distinct.Any(l => l.Length is 0 or > 64 || l.Contains('|', StringComparison.Ordinal)))
                return RandomizationErrors.InvalidScheme($"Stratification factor '{name}' needs at least one level of 1 to 64 characters without '|'.");
            if (!normalizedFactors.TryAdd(name, distinct))
                return RandomizationErrors.InvalidScheme($"Stratification factor '{name}' is listed twice.");
        }

        var sizes = (blockSizes ?? []).Distinct().Order().ToList();
        int ratioSum = normalizedArms.Values.Sum();
        int listSize = 0;
        switch (method)
        {
            case RandomizationMethod.PermutedBlock:
                if (sizes.Count == 0)
                    sizes.Add(ratioSum);
                if (sizes.Any(s => s < ratioSum || s % ratioSum != 0))
                    return RandomizationErrors.InvalidScheme($"Every block size must be a positive multiple of the ratio sum ({ratioSum}).");
                if (slotsPerStratum < 1)
                    return RandomizationErrors.InvalidScheme("A permuted-block scheme needs at least one slot per stratum.");
                long strata = normalizedFactors.Values.Aggregate(1L, (n, levels) => n * levels.Count);
                if (strata * slotsPerStratum > MaxListSize)
                    return RandomizationErrors.InvalidScheme($"The list would hold {strata * slotsPerStratum} slots; the limit is {MaxListSize}.");
                listSize = (int)(strata * slotsPerStratum);
                preferredArmProbability = 0;
                break;
            case RandomizationMethod.Minimization:
                if (normalizedFactors.Count == 0)
                    return RandomizationErrors.InvalidScheme("A minimisation scheme needs at least one stratification factor.");
                if (preferredArmProbability is <= 0.5 or > 1)
                    return RandomizationErrors.InvalidScheme("The preferred-arm probability must be above 0.5 and at most 1.");
                sizes.Clear();
                slotsPerStratum = 0;
                break;
            default:
                return RandomizationErrors.InvalidScheme($"Unknown randomisation method '{method}'.");
        }

        var scheme = new RandomizationScheme
        {
            Id = Guid.NewGuid(),
            StudyCode = studyCode,
            Version = version,
            Method = method,
            Status = RandomizationSchemeStatus.Draft,
            ArmsJson = JsonSerializer.Serialize(normalizedArms),
            StratificationFactorsJson = JsonSerializer.Serialize(normalizedFactors),
            BlockSizesJson = JsonSerializer.Serialize(sizes),
            SlotsPerStratum = slotsPerStratum,
            PreferredArmProbability = preferredArmProbability,
            RequiresKit = requiresKit,
            DraftedBy = draftedBy,
            DraftedAt = clock.UtcNow,
            ListSize = listSize,
            CreatedBy = draftedBy,
            ReasonForChange = reasonForChange,
        };
        scheme.ConfigurationHash = scheme.ComputeConfigurationHash();
        return scheme;
    }

    /// <summary>
    /// Signs the version off for go-live. The approver must be someone other than the person who drafted it
    /// (separation of duties). The caller retires the previous approved version and generates the list in the same
    /// transaction.
    /// </summary>
    public Result<bool> Approve(string approvedBy, string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var allowed = CanApprove(approvedBy);
        if (allowed.IsFailure)
            return allowed;

        Status = RandomizationSchemeStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = clock.UtcNow;
        ApprovalReason = reason;
        ReasonForChange = reason;
        return true;
    }

    /// <summary>Whether <paramref name="approvedBy"/> may approve this version now, without changing it.</summary>
    public Result<bool> CanApprove(string approvedBy)
    {
        if (Status != RandomizationSchemeStatus.Draft)
            return RandomizationErrors.SchemeNotDraft(Version, Status);
        if (string.Equals(approvedBy, DraftedBy, StringComparison.OrdinalIgnoreCase))
            return RandomizationErrors.SelfApproval();
        if (ComputeConfigurationHash() != ConfigurationHash)
            return RandomizationErrors.ConfigurationTampered(Version);
        return true;
    }

    /// <summary>Retires an approved version when a later one is approved.</summary>
    public void Retire(string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status != RandomizationSchemeStatus.Approved)
            return;
        Status = RandomizationSchemeStatus.Retired;
        RetiredAt = clock.UtcNow;
        ReasonForChange = reason;
    }

    /// <summary>Every stratum of the scheme, in canonical order (the cartesian product of the factor levels).</summary>
    public IReadOnlyList<string> Strata()
    {
        var factors = StratificationFactors();
        if (factors.Count == 0)
            return [DefaultStratum];

        IEnumerable<string> keys = [string.Empty];
        foreach (var (factor, levels) in factors.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            keys = keys.SelectMany(prefix => levels.Select(level =>
                prefix.Length == 0 ? $"{factor}={level}" : $"{prefix}|{factor}={level}")).ToList();
        }

        return keys.ToList();
    }

    /// <summary>
    /// The canonical stratum key for a participant's factor values, or a validation error when a factor is missing,
    /// a level is not declared, or a value names a factor the scheme does not stratify by.
    /// </summary>
    public Result<string> StratumFor(IReadOnlyDictionary<string, string>? values)
    {
        var factors = StratificationFactors();
        values ??= new Dictionary<string, string>();
        if (factors.Count == 0)
        {
            return values.Count == 0 ? DefaultStratum : RandomizationErrors.UnknownFactor(values.Keys.First());
        }

        foreach (string supplied in values.Keys)
        {
            if (!factors.ContainsKey(supplied))
                return RandomizationErrors.UnknownFactor(supplied);
        }

        var parts = new List<string>();
        foreach (var (factor, levels) in factors.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!values.TryGetValue(factor, out string? value) || string.IsNullOrWhiteSpace(value))
                return RandomizationErrors.MissingFactor(factor);
            string level = value.Trim();
            if (!levels.Contains(level, StringComparer.Ordinal))
                return RandomizationErrors.UnknownLevel(factor, level);
            parts.Add($"{factor}={level}");
        }

        return string.Join('|', parts);
    }

    /// <summary>
    /// SHA-256 over the configuration in canonical form. The JSON columns are re-read rather than hashed as stored,
    /// because PostgreSQL <c>jsonb</c> rewrites their spacing and key order.
    /// </summary>
    private string ComputeConfigurationHash()
    {
        string arms = string.Join(',', Arms().OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}:{a.Value}"));
        string factors = string.Join(';', StratificationFactors().OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={string.Join(',', f.Value)}"));
        string blocks = string.Join(',', BlockSizes().Select(b => b.ToString(CultureInfo.InvariantCulture)));
        string canonical = string.Join('\n',
            "cadence.randomization.scheme.v1",
            StudyCode.Value,
            Version.ToString(CultureInfo.InvariantCulture),
            Method.ToString(),
            arms,
            factors,
            blocks,
            SlotsPerStratum.ToString(CultureInfo.InvariantCulture),
            PreferredArmProbability.ToString("R", CultureInfo.InvariantCulture),
            RequiresKit ? "kit" : "no-kit");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
