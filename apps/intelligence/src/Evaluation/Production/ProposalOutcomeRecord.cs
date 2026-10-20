using System;

namespace Cadence.Modules.Intelligence.Evaluation.Production;

/// <summary>What a person did with an agent's Proposal (ADR-2489).</summary>
public enum ProposalDecision
{
    /// <summary>Approved with an attestation over its content hash.</summary>
    Approved,

    /// <summary>Rejected, with a reason.</summary>
    Rejected,

    /// <summary>Expired or withdrawn without a decision; counted in the window but not in the acceptance rate.</summary>
    Lapsed,
}

/// <summary>The coded reason a reviewer gave for a rejection (ADR-2634 §4.5).</summary>
public enum RejectionReason
{
    /// <summary>The content was wrong.</summary>
    Incorrect,

    /// <summary>The content was not supported by the cited sources.</summary>
    UnsupportedBySource,

    /// <summary>The content left out something it needed.</summary>
    Incomplete,

    /// <summary>The proposal addressed the wrong participant, site or object.</summary>
    WrongScope,

    /// <summary>Acting on the proposal could have harmed a participant or the study's integrity.</summary>
    Unsafe,

    /// <summary>Any other reason, with free text kept on the Proposal.</summary>
    Other,
}

/// <summary>
/// The production outcome of one agent Proposal, per configuration version (ADR-2634 §4.5). The Intelligence module
/// writes one when the kernel reports the Proposal approved, rejected, withdrawn or expired.
/// </summary>
/// <param name="ProposalId">The kernel Proposal.</param>
/// <param name="Configuration">The configuration version that wrote it.</param>
/// <param name="Decision">What the reviewer did.</param>
/// <param name="RejectionReason">The coded reason, for a rejection.</param>
/// <param name="EditDistance">
/// Normalized edit distance (0 to 1) between what the agent drafted and what was approved; 0 when approved unchanged,
/// null when not approved.
/// </param>
/// <param name="CitationsDropped">Citations the verifier removed before the Proposal was written.</param>
/// <param name="Cost">The run's cost.</param>
/// <param name="Latency">The run's latency.</param>
/// <param name="DecidedAt">When the decision was recorded, from <c>IClock</c>.</param>
public sealed record ProposalOutcomeRecord(
    Guid ProposalId,
    ConfigurationRef Configuration,
    ProposalDecision Decision,
    RejectionReason? RejectionReason,
    double? EditDistance,
    int CitationsDropped,
    decimal Cost,
    TimeSpan Latency,
    DateTimeOffset DecidedAt)
{
    /// <summary>Builds an approved outcome, computing the edit distance from the drafted and approved text.</summary>
    public static ProposalOutcomeRecord Approved(
        Guid proposalId, ConfigurationRef configuration, string drafted, string approved, int citationsDropped, decimal cost, TimeSpan latency, DateTimeOffset decidedAt) =>
        new(proposalId, configuration, ProposalDecision.Approved, null, Production.EditDistance.Normalized(drafted, approved), citationsDropped, cost, latency, decidedAt);

    /// <summary>Builds a rejected outcome.</summary>
    public static ProposalOutcomeRecord Rejected(
        Guid proposalId, ConfigurationRef configuration, RejectionReason reason, int citationsDropped, decimal cost, TimeSpan latency, DateTimeOffset decidedAt) =>
        new(proposalId, configuration, ProposalDecision.Rejected, reason, null, citationsDropped, cost, latency, decidedAt);
}

/// <summary>Character-level Levenshtein distance, normalized by the longer text.</summary>
public static class EditDistance
{
    /// <summary>
    /// The edit distance between <paramref name="drafted"/> and <paramref name="approved"/> divided by the longer
    /// length: 0 for identical text, 1 when nothing survived.
    /// </summary>
    public static double Normalized(string drafted, string approved)
    {
        ArgumentNullException.ThrowIfNull(drafted);
        ArgumentNullException.ThrowIfNull(approved);
        var longer = Math.Max(drafted.Length, approved.Length);
        return longer == 0 ? 0 : Levenshtein(drafted, approved) / (double)longer;
    }

    /// <summary>The number of single-character insertions, deletions and substitutions between two texts.</summary>
    public static int Levenshtein(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length < b.Length)
        {
            (a, b) = (b, a);
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
