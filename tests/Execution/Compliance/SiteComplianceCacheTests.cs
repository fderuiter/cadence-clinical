using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Compliance;

/// <summary>
/// Coverage for the `MIG-SUBJ-006` `SiteComplianceCache` aggregate: factory initial-state assertions,
/// the plain upsert-increment (never compare-and-swap) semantics of `ApplyWebhookUpdate`, full-replacement
/// (not merge) of `MissingDocuments`, and the defensive-encapsulation boundary around the exposed
/// `IReadOnlyList&lt;string&gt;`.
/// @req:PRD-SUB-001
/// </summary>
public class SiteComplianceCacheTests
{
    private static readonly StudyCode Study = new("STUDY-001");
    private static readonly SiteCode Site = new("SITE-042");
    private const string Milestone = "SITE_ACTIVATION";

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_InitialState_HasVersionOneAndSuppliedMissingDocuments()
    {
        var missingDocuments = new[] { "1572", "CV" };

        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, missingDocuments);

        cache.Version.Should().Be(1);
        cache.StudyCode.Should().Be(Study);
        cache.SiteCode.Should().Be(Site);
        cache.Milestone.Should().Be(Milestone);
        cache.IsComplete.Should().BeFalse();
        cache.MissingDocuments.Should().Equal(missingDocuments);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection()
    {
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "1572" });

        cache.ApplyWebhookUpdate(isComplete: false, missingDocuments: new[] { "1572", "CV" });
        cache.Version.Should().Be(2);

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: Array.Empty<string>());
        cache.Version.Should().Be(3);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList()
    {
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false,
            new[] { "1572", "CV", "IRB approval" });

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: Array.Empty<string>());

        cache.IsComplete.Should().BeTrue();
        cache.MissingDocuments.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents()
    {
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "1572" });

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "CV" });

        // A merge/append bug would retain "1572" alongside "CV"; the spec requires a full replace.
        cache.MissingDocuments.Should().Equal("CV");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState()
    {
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "1572" });

        IReadOnlyList<string> exposed = cache.MissingDocuments;

        // Attempt exactly the defensive-encapsulation attack the acceptance criterion describes: cast the
        // exposed IReadOnlyList<string> back to a mutable List<string> and mutate it from outside the
        // aggregate. The backing field must never be handed out under a widened type, so this cast is
        // expected to fail outright rather than silently succeed and expose a live reference.
        Action castAndMutate = () =>
        {
            var mutable = (List<string>)exposed;
            mutable.Add("INJECTED");
        };

        castAndMutate.Should().Throw<InvalidCastException>();

        cache.MissingDocuments.Should().Equal("1572");
        cache.MissingDocuments.Should().NotContain("INJECTED");
    }
}
