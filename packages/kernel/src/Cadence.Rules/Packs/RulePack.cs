using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;

namespace Cadence.Rules.Packs;

/// <summary>Who issued a rule (spec §10.1). Public authorities publish standards; sponsor and study rules are the tenant's own.</summary>
public enum RuleAuthority
{
    /// <summary>CDISC (conformance rules for SDTM, SEND, ADaM, Define-XML).</summary>
    Cdisc,
    /// <summary>US Food and Drug Administration.</summary>
    Fda,
    /// <summary>Japan's Pharmaceuticals and Medical Devices Agency.</summary>
    Pmda,
    /// <summary>China's National Medical Products Administration.</summary>
    Nmpa,
    /// <summary>European Medicines Agency.</summary>
    Ema,
    /// <summary>The sponsor's own standards.</summary>
    Sponsor,
    /// <summary>One study's own rules, such as its edit checks.</summary>
    Study,
}

/// <summary>
/// The public document a rule implements (K-RULE-2): for example the CDISC SDTMIG conformance rules, an FDA
/// validator rule or a technical conformance guide section. Sponsor and study rules cite the sponsor's own document.
/// </summary>
public sealed record SourceCitation(string Document, string? Section, string? Uri);

/// <summary>
/// Where a rule applies. A null field matches anything. <see cref="Dataset"/> is compared with the name of the
/// dataset, domain or record type being checked; <see cref="Variable"/> optionally names the target variable or item.
/// </summary>
public sealed record RuleApplicability(
    ValidationStage Stage,
    string? Standard,
    string? StandardVersion,
    string? Agency,
    string? Dataset,
    string? Variable = null);

/// <summary>The dates a rule applies to: from <see cref="From"/> (inclusive) until <see cref="Until"/> (exclusive).</summary>
public sealed record EffectivePeriod(DateOnly? From, DateOnly? Until)
{
    /// <summary>A period with no start and no end.</summary>
    public static EffectivePeriod Always { get; } = new(null, null);

    /// <summary>True when <paramref name="date"/> is on or after <see cref="From"/> and before <see cref="Until"/>.</summary>
    public bool Contains(DateOnly date) => (From is not { } from || date >= from) && (Until is not { } until || date < until);
}

/// <summary>
/// One versioned rule (spec §10.1). A record violates the rule when <see cref="Condition"/> evaluates to true;
/// false and unknown raise nothing. <see cref="Explanation"/> is the message a finding carries, written by the pack's
/// author from the cited document, never copied from a vendor's implementation (K-RULE-2).
/// </summary>
public sealed record Rule(
    string Id,
    int Version,
    RuleAuthority Authority,
    SourceCitation Citation,
    RuleApplicability Applicability,
    ValidationSeverity Severity,
    RuleExpression Condition,
    string Explanation,
    string? RemediationGuidance,
    EffectivePeriod Effective);

/// <summary>
/// A set of rules released together (spec §10.1). A pack is the content of a Metadata Registry library item of kind
/// <see cref="LibraryItemKind.RulePack"/>: its version and release Attestation are the library item's, so a pack
/// changes only by drafting and approving a new version (K-RULE-1).
/// </summary>
[JsonConverter(typeof(RulePackJsonConverter))]
public sealed record RulePack(RuleAuthority Authority, string Title, IReadOnlyList<Rule> Rules);

/// <summary>A released rule pack together with the library item version it was read from.</summary>
public sealed record LoadedRulePack(LibraryItemId Id, string Key, int Version, string ContentHash, GovernanceStatus Status, RulePack Pack);
