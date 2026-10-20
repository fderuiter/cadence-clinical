using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Grounding;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The citation verifier (ADR-2633 §4.6, K-AI-4): a claim or value that does not trace to what the run read is dropped
/// before a person sees it.
/// </summary>
public sealed class CitationVerifierTests
{
    private readonly IntelligenceWorld world = new();

    [Fact]
    [KernelRule("K-AI-4")]
    public async Task AFabricatedValue_IsDropped_AndTheTrueOneKept()
    {
        var session = await ReadPotassiumAsync();
        var output = Output(
            Claim("Potassium is 6.2 mmol/L, above range.", IntelligenceWorld.Potassium.ToString(), Quote(IntelligenceWorld.Potassium, """{"type":"quantity","value":6.2,"unit":"mmol/L"}""")),
            Claim("Potassium is 7.9 mmol/L, critical.", IntelligenceWorld.Potassium.ToString(), Quote(IntelligenceWorld.Potassium, """{"type":"quantity","value":7.9,"unit":"mmol/L"}""")));

        var grounded = CitationVerifier.Verify(output, session.ReadSet);

        grounded.Claims.Should().ContainSingle().Which.Text.Should().StartWith("Potassium is 6.2");
        grounded.Dropped.Should().ContainSingle().Which.Should().Match<DroppedItem>(d => d.Index == 1 && d.Reason == DropReason.ValueMismatch);
    }

    [Fact]
    public async Task AValueQuotedInADifferentKeyOrderOrNumberSpelling_StillMatches()
    {
        var session = await ReadPotassiumAsync();
        var output = Output(Claim("Potassium is high.", IntelligenceWorld.Potassium.ToString(),
            Quote(IntelligenceWorld.Potassium, """{ "unit": "mmol/L", "value": 6.20, "type": "quantity" }""")));

        CitationVerifier.Verify(output, session.ReadSet).Claims.Should().ContainSingle();
    }

    [Fact]
    public async Task AValueForAnObjectWhoseValueWasNeverShown_IsDropped()
    {
        var session = await ReadPotassiumAsync();
        await AgentToolTests.CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Site.ToString() });
        var output = Output(Claim("Site 101 enrolled 40.", IntelligenceWorld.Site.ToString(), Quote(IntelligenceWorld.Site, "40")));

        CitationVerifier.Verify(output, session.ReadSet).Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.ValueNotRead);
    }

    [Fact]
    public async Task AValueOfAnUncitedObject_IsDropped()
    {
        var session = await ReadPotassiumAsync();
        var output = Output(new AgentClaim("Potassium is 6.2.", [IntelligenceWorld.Site.ToString()],
            [Quote(IntelligenceWorld.Potassium, """{"type":"quantity","value":6.2,"unit":"mmol/L"}""")]));

        CitationVerifier.Verify(output, session.ReadSet).Dropped.Single().Reason.Should().BeOneOf(DropReason.CitationNotRead, DropReason.ValueNotCited);
    }

    [Fact]
    [KernelRule("K-AI-4")]
    public async Task UncitedClaims_AndClaimsCitingWhatWasNotRead_AreDropped()
    {
        var session = await ReadPotassiumAsync();
        var output = Output(
            new AgentClaim("The site is behind on SDV.", []),
            Claim("Sodium is normal.", IntelligenceWorld.Sodium.ToString()),
            Claim("Cites garbage.", "not a ref"),
            Claim("Potassium was recorded.", IntelligenceWorld.Potassium.ToString()));

        var grounded = CitationVerifier.Verify(output, session.ReadSet);

        grounded.Claims.Select(c => c.Text).Should().Equal("Potassium was recorded.");
        grounded.Dropped.Select(d => d.Reason).Should().Equal(DropReason.Uncited, DropReason.CitationNotRead, DropReason.CitationInvalid);
    }

    [Fact]
    public async Task ACitationPinnedToAnotherVersion_IsNotRead()
    {
        var session = await ReadPotassiumAsync();
        var pinnedToV1 = $"{IntelligenceWorld.Potassium.Kind}:{IntelligenceWorld.Potassium.Id}@1";
        var unpinned = $"{IntelligenceWorld.Potassium.Kind}:{IntelligenceWorld.Potassium.Id}";

        var grounded = CitationVerifier.Verify(Output(Claim("Old value.", pinnedToV1), Claim("Current value.", unpinned)), session.ReadSet);

        IntelligenceWorld.Potassium.Version.Should().Be(2, "the run read version 2");
        grounded.Claims.Select(c => c.Text).Should().Equal("Current value.");
        grounded.Dropped.Single().Reason.Should().Be(DropReason.CitationNotRead);
    }

    [Fact]
    public async Task AffectedObjectsNotRead_AreDropped()
    {
        var session = await ReadPotassiumAsync();
        var output = Output(Claim("Potassium was recorded.", IntelligenceWorld.Potassium.ToString())) with
        {
            Affected = [IntelligenceWorld.Potassium.ToString(), IntelligenceWorld.Sodium.ToString(), IntelligenceWorld.Potassium.ToString()],
        };

        var grounded = CitationVerifier.Verify(output, session.ReadSet);

        grounded.Affected.Should().Equal(IntelligenceWorld.Potassium);
        grounded.Dropped.Should().ContainSingle(d => d.Kind == DroppedItemKind.Affected && d.Reason == DropReason.AffectedNotRead);
    }

    [Fact]
    public async Task AnObjectMaskedFromTheAgent_CannotBeCited()
    {
        world.Visibility.Hide(IntelligenceWorld.Agent, IntelligenceWorld.Potassium);
        var session = await ReadPotassiumAsync();

        var grounded = CitationVerifier.Verify(Output(Claim("Potassium is high.", IntelligenceWorld.Potassium.ToString())), session.ReadSet);

        grounded.IsGrounded.Should().BeFalse();
    }

    private async Task<AgentToolSession> ReadPotassiumAsync()
    {
        var session = IntelligenceWorld.Open(world.Services());
        await AgentToolTests.CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Potassium.ToString() });
        return session;
    }

    internal static AgentRunOutput Output(params AgentClaim[] claims) => new("Raise a query on the high potassium.", claims, [], ["query.raise"]);

    internal static AgentClaim Claim(string text, string citation, params QuotedValue[] values) => new(text, [citation], values);

    internal static QuotedValue Quote(Cadence.Kernel.References.NodeRef node, string json) => new(node.ToString(), JsonElement.Parse(json));
}
