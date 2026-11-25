using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// What an Assertion states (spec §8.2). Assertions are append-only: a later assertion of the same kind retracts an
/// earlier one, and nothing about the observation changes (ADR-2621).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssertionKind>))]
public enum AssertionKind
{
    /// <summary>Source data verification of one revision of an observation's value.</summary>
    Verified,

    /// <summary>Source data review of one revision of an observation's value.</summary>
    Reviewed,

    /// <summary>A query about an observation; answered by <see cref="QueryAnswered"/>, closed by its retraction.</summary>
    Queried,

    /// <summary>An answer to a <see cref="Queried"/> assertion.</summary>
    QueryAnswered,

    /// <summary>A dictionary code assigned to one revision of an observation's value.</summary>
    CodedAs,

    /// <summary>An adjudication of one revision of an observation's value; several adjudicators may assert it.</summary>
    Adjudicated,

    /// <summary>Data entry is frozen in a scope: no observation is recorded or corrected there (spec §10.5).</summary>
    Frozen,

    /// <summary>Everything in a scope is locked: no observation is recorded or corrected, and nothing more is asserted.</summary>
    Locked,
}

/// <summary>The two kinds of lock (spec §10.5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LockKind>))]
public enum LockKind
{
    /// <summary>See <see cref="AssertionKind.Frozen"/>.</summary>
    Frozen = AssertionKind.Frozen,

    /// <summary>See <see cref="AssertionKind.Locked"/>.</summary>
    Locked = AssertionKind.Locked,
}

/// <summary>
/// The scopes a lock can cover, from the widest to the narrowest (spec §10.5, ADR-2621): study, study site, participant
/// enrollment, form instance (its <c>CollectionInstance</c>, K-FACT-6) and observation. A write that a lock can cover
/// takes shared advisory locks on its covering scopes in this order; a lock command takes the exclusive one on its own
/// scope.
/// </summary>
public static class LockScopes
{
    /// <summary>The kinds of scope, widest first.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
        [NodeKinds.Study, NodeKinds.StudySite, NodeKinds.Enrollment, NodeKinds.CollectionInstance, NodeKinds.Observation];

    /// <summary>True when <paramref name="scope"/> names an object a lock can cover, without a version.</summary>
    public static bool IsScope(NodeRef? scope) =>
        scope is { Version: null } && Kinds.Contains(scope.Kind) && scope.Id != Guid.Empty;

    /// <summary>Zero-based precedence order of <paramref name="kind"/> from widest scope to narrowest, or <see cref="Kinds"/>.<c>Count</c> when unknown.</summary>
    public static int OrderOf(string kind)
    {
        for (var i = 0; i < Kinds.Count; i++)
        {
            if (string.Equals(Kinds[i], kind, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Kinds.Count;
    }

    /// <summary>The scopes covering a study or study site, widest first.</summary>
    public static IReadOnlyList<NodeRef> Covering(StudyId study, StudySiteId? studySite = null) =>
        studySite is { } site ? [NodeRef.For(study), NodeRef.For(site)] : [NodeRef.For(study)];

    /// <summary>
    /// The scopes covering an observation of an enrollment, widest first: its form instance when it was collected in one,
    /// and the observations themselves when named.
    /// </summary>
    public static IReadOnlyList<NodeRef> Covering(
        StudyId study, StudySiteId studySite, EnrollmentId enrollment, ObservationId? observation = null, CollectionInstanceId? instance = null) =>
        Covering(study, studySite, enrollment, instance, observation is { } id ? [id] : []);

    /// <summary>The scopes covering <paramref name="observations"/> of a form instance, widest first.</summary>
    public static IReadOnlyList<NodeRef> Covering(
        StudyId study, StudySiteId studySite, EnrollmentId enrollment, CollectionInstanceId? instance, IEnumerable<ObservationId> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        List<NodeRef> scopes = [NodeRef.For(study), NodeRef.For(studySite), NodeRef.For(enrollment)];
        if (instance is { } form)
        {
            scopes.Add(NodeRef.For(form));
        }

        scopes.AddRange(observations.Distinct().Order().Select(o => NodeRef.For(o)));
        return scopes;
    }
}

/// <summary>
/// The content a lock's signer attests (spec §10.5): applying a lock of a kind at a scope, or releasing one lock. The
/// attestation targets the Assertion that will record it, <c>assertion:&lt;id&gt;</c>, with the hash of <see cref="Apply"/> or <see cref="Release"/> as its
/// content hash, so the signature covers exactly that statement (21 CFR 11.70). It is signed before the statement is
/// recorded, the way a finding decision is (K-FIND-2).
/// </summary>
public static class LockStatement
{
    /// <summary>The canonical JSON of applying a <paramref name="kind"/> lock at <paramref name="scope"/>.</summary>
    public static string Apply(AssertionId assertion, LockKind kind, NodeRef scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ContentHash.Canonicalize(new JsonObject
        {
            ["action"] = "apply",
            ["assertion"] = assertion.ToString(),
            ["kind"] = kind.ToString(),
            ["scope"] = scope.ToString(),
        }.ToJsonString());
    }

    /// <summary>The canonical JSON of releasing the lock <paramref name="released"/>.</summary>
    public static string Release(AssertionId assertion, AssertionId released) =>
        ContentHash.Canonicalize(new JsonObject
        {
            ["action"] = "release",
            ["assertion"] = assertion.ToString(),
            ["released"] = released.ToString(),
        }.ToJsonString());

    /// <summary>What the signer attests for <paramref name="canonical"/>, recorded as <paramref name="assertion"/>.</summary>
    public static AttestationTarget Target(AssertionId assertion, string canonical) =>
        new(NodeKinds.Assertion, assertion.Value, ContentHash.OfCanonical(canonical));
}

/// <summary>Text limits for assertions.</summary>
public static class AssertionText
{
    /// <summary>The longest query or answer text.</summary>
    public const int MaxLength = 4000;
}
