using System;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.Knowledge;

/// <summary>
/// What a fact or specification points at to say what it means (spec §6.1): one Concept at one version. Observations,
/// CollectionSpec items, PlannedActivity assessments and standards variable mappings all carry one (K-SEM-1). There is
/// no "latest" form: a reference without a version is not a reference.
/// </summary>
public readonly record struct ConceptRef
{
    /// <param name="conceptId">The concept.</param>
    /// <param name="version">The concept version the reference was made against, starting at 1.</param>
    /// <remarks>
    /// JSON reads through this constructor, so a document cannot produce the empty reference a struct's default
    /// constructor would give.
    /// </remarks>
    [JsonConstructor]
    public ConceptRef(ConceptId conceptId, int version)
    {
        if (conceptId.IsEmpty)
        {
            throw new ArgumentException("A ConceptRef names a concept.", nameof(conceptId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ConceptId = conceptId;
        Version = version;
    }

    public ConceptId ConceptId { get; }

    public int Version { get; }

    public static ConceptRef New() => new(ConceptId.New(), 1);

    public static ConceptRef Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return New();
        var parts = text.Split('@');
        return parts.Length == 2 && int.TryParse(parts[1], out var v)
            ? new ConceptRef(ConceptId.Parse(parts[0]), v)
            : New();
    }

    public override string ToString() => $"{ConceptId}@{Version}";
}

/// <summary>
/// A code in a pinned code-system version, for example a LOINC code in LOINC 2.78 or an LLT in MedDRA 27.1. Coding a
/// verbatim term (MedDRA, WHODrug) is an Assertion carrying one of these about an Observation, never a change to the
/// Observation (spec §6.1).
/// </summary>
public readonly record struct Coding
{
    public Coding(CodeSystemVersionId codeSystemVersionId, string code)
    {
        if (codeSystemVersionId.IsEmpty)
        {
            throw new ArgumentException("A coding names the code-system version it was taken from.", nameof(codeSystemVersionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        CodeSystemVersionId = codeSystemVersionId;
        Code = code;
    }

    public CodeSystemVersionId CodeSystemVersionId { get; }

    public string Code { get; }

    public override string ToString() => $"{CodeSystemVersionId}#{Code}";
}
