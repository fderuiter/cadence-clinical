using System;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The public object reference <c>kind:id@version</c> (ADR-2481, spec §3.4): one canonical string per object version,
/// strict parsing, and a registered kind for every kernel identifier.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NodeRefTests
{
    private static readonly Guid Id = Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e");

    /// <summary>
    /// A reference formats to its canonical string and parses back to an equal reference, with and without a
    /// version, including through JSON.
    /// </summary>
    [Theory(DisplayName = "NodeRef_RoundTripsItsCanonicalForm")]
    [InlineData("study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3")]
    [InlineData("participant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    [InlineData("tenant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@9223372036854775807")]
    public void NodeRefRoundTripsItsCanonicalForm(string text)
    {
        var node = NodeRef.Parse(text);

        node.ToString().Should().Be(text);
        NodeRef.Parse(node.ToString()).Should().Be(node);

        var json = JsonSerializer.Serialize(new { node });
        json.Should().Be($"{{\"node\":\"{text}\"}}");
        JsonSerializer.Deserialize<NodeRef>($"\"{text}\"").Should().Be(node);
    }

    /// <summary>
    /// Only the canonical string parses, so two strings never name the same object version: unregistered or
    /// malformed kinds, upper-case or braced UUIDs, the empty UUID, and zero, signed or zero-padded versions fail.
    /// </summary>
    [Theory(DisplayName = "NodeRef_RejectsNonCanonicalStrings")]
    [KernelRule("K-ID-6")]
    [InlineData("")]
    [InlineData("study")]
    [InlineData("study:")]
    [InlineData(":0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    [InlineData("Study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    [InlineData("studysite:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    [InlineData("widget:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    [InlineData("study:0192F3C4-5E6A-7B8C-9D0E-1F2A3B4C5D6E")]
    [InlineData("study:{0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e}")]
    [InlineData("study:0192f3c45e6a7b8c9d0e1f2a3b4c5d6e")]
    [InlineData("study:00000000-0000-0000-0000-000000000000")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@0")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@03")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@+3")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@-3")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3@4")]
    [InlineData("study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@9223372036854775808")]
    [InlineData(" study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")]
    public void NodeRefRejectsNonCanonicalStrings(string text)
    {
        NodeRef.TryParse(text, out _).Should().BeFalse();
        var parse = () => NodeRef.Parse(text);
        parse.Should().Throw<FormatException>();
        var json = () => JsonSerializer.Deserialize<NodeRef>(JsonSerializer.Serialize(text));
        json.Should().Throw<JsonException>();
    }

    /// <summary>Typed factories and accessors bind the kind to the identifier type and nothing else.</summary>
    [Fact(DisplayName = "NodeRef_BindsKindToIdentifierType")]
    public void NodeRefBindsKindToIdentifierType()
    {
        var studySite = StudySiteId.FromGuid(Id);
        var node = NodeRef.For(studySite, version: 2);

        node.Kind.Should().Be(NodeKinds.StudySite);
        node.Should().Be(NodeRef.For((IEntityId)studySite, 2));
        node.TryGetId<StudySiteId>(out var back).Should().BeTrue();
        back.Should().Be(studySite);
        node.TryGetId<StudyId>(out _).Should().BeFalse();
        node.Unversioned().Should().Be(new NodeRef(NodeKinds.StudySite, Id));
        node.Unversioned().AtVersion(2).Should().Be(node);
    }

    /// <summary>Constructing a reference validates it the same way parsing does.</summary>
    [Fact(DisplayName = "NodeRef_ConstructorRejectsInvalidParts")]
    public void NodeRefConstructorRejectsInvalidParts()
    {
        FluentActions.Invoking(() => new NodeRef("widget", Id)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeRef(NodeKinds.Study, Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new NodeRef(NodeKinds.Study, Id, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => NodeRef.For(StudyId.Empty)).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Every strongly-typed kernel identifier has exactly one well-formed kind, so any kernel object can be
    /// referenced. Adding an identifier without registering its kind fails here.
    /// </summary>
    [Fact(DisplayName = "NodeKinds_CoverEveryKernelIdentifier")]
    [KernelRule("K-ID-6")]
    public void NodeKindsCoverEveryKernelIdentifier()
    {
        var identifierTypes = typeof(TenantId).Assembly.GetTypes()
            .Where(t => t is { IsValueType: true, IsPublic: true }
                && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityId<>)))
            .ToList();

        identifierTypes.Should().NotBeEmpty();
        NodeKinds.IdentifierTypes.Should().BeEquivalentTo(identifierTypes);
        NodeKinds.All.Should().OnlyHaveUniqueItems().And.OnlyContain(k => NodeKinds.IsWellFormed(k));
        NodeKinds.All.Should().OnlyContain(k => NodeKinds.Of(NodeKinds.IdentifierTypeOf(k)!) == k);
    }

    /// <summary>Kind syntax: lower snake_case starting with a letter.</summary>
    [Theory(DisplayName = "NodeKinds_WellFormedness")]
    [InlineData("study", true)]
    [InlineData("study_site", true)]
    [InlineData("rule_pack2", true)]
    [InlineData("", false)]
    [InlineData("Study", false)]
    [InlineData("_study", false)]
    [InlineData("study_", false)]
    [InlineData("study__site", false)]
    [InlineData("2study", false)]
    [InlineData("study-site", false)]
    public void NodeKindsWellFormedness(string kind, bool expected) =>
        NodeKinds.IsWellFormed(kind).Should().Be(expected);
}
