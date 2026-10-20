using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Deid;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Guard;
using Cadence.Modules.Intelligence.Inference.Routing;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.AI;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The outbound guard (ADR-2636 §4.3): identity-layer and uninspectable content refuse the call before anything is
/// sent; direct identifiers in free text and tool results are replaced one way; clinical dates and kernel references
/// stay; the response carries counts, never values.
/// </summary>
public sealed class OutboundGuardTests
{
    private readonly StubProvider _provider = new("stub", InferenceLocality.External, "stub");

    /// <summary>
    /// Content marked as identity-layer data, at message or content level, refuses the call and the provider is
    /// never called (IdentityVault data never reaches a model).
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task IdentityLayerContent_RefusesTheCall_BeforeAnythingIsSent()
    {
        var guard = Guard();
        var markedContent = new ChatMessage(ChatRole.User, [IdentityLayerMark.Mark(new TextContent("Jane Roe"), "Person")]);
        var markedMessage = new ChatMessage(ChatRole.User, "Participant details")
        {
            AdditionalProperties = new() { [IdentityLayerMark.Key] = "SiteLocal" },
        };

        foreach (var message in new[] { markedContent, markedMessage })
        {
            var call = () => guard.GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);
            (await call.Should().ThrowAsync<InferenceRefusedException>()).Which.Reason.Should().Be(InferenceRefusalReason.IdentityLayerContent);
        }

        _provider.Calls.Should().BeEmpty();
    }

    /// <summary>Binary or linked content the guard cannot inspect is refused rather than sent unchecked.</summary>
    [Fact]
    public async Task UninspectableContent_IsRefused()
    {
        var message = new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);

        var call = () => Guard().GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);

        (await call.Should().ThrowAsync<InferenceRefusedException>()).Which.Reason.Should().Be(InferenceRefusalReason.UninspectableContent);
        _provider.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// Direct identifiers in free text, system instructions and tool results are replaced with placeholders before the
    /// request leaves; visit dates and kernel references stay, and the response records counts by category only.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task DirectIdentifiers_AreScrubbedOneWay_DatesAndReferencesStay()
    {
        var toolResult = JsonSerializer.SerializeToElement(new
        {
            nodeRef = "observation/0192f7a0-1b2c-7d3e-8f40-123456789abc",
            note = "Coordinator wrote: call back on 555-867-5309",
            visitDate = "2026-03-14",
        });
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email jane.roe@example.org about the visit on 2026-03-14."),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)]),
        };

        var response = await Guard().GetResponseAsync(
            messages, new ChatOptions { Instructions = "Escalate to safety@example.org." }, TestContext.Current.CancellationToken);

        var sent = _provider.Calls.Single();
        sent[0].Text.Should().Be("Email [EMAIL] about the visit on 2026-03-14.");
        var result = JsonSerializer.Serialize(((FunctionResultContent)sent[1].Contents[0]).Result);
        result.Should().Contain("observation/0192f7a0-1b2c-7d3e-8f40-123456789abc")
            .And.Contain("2026-03-14")
            .And.Contain("[TELEPHONE_FAX]")
            .And.NotContain("867-5309");
        var counts = (IReadOnlyDictionary<string, int>)response.AdditionalProperties![OutboundGuardChatClient.ScrubCountsKey]!;
        counts.Should().Contain(DetectorCategory.Email, 2).And.Contain(DetectorCategory.TelephoneFax, 1);
        string.Join(" ", counts.Keys).Should().NotContain("example.org");
        messages[0].Text.Should().Contain("jane.roe@example.org", "the caller's messages are not changed in place");
    }

    /// <summary>The model's own reasoning and tool calls are replayed unchanged.</summary>
    [Fact]
    public async Task ModelOutput_IsReplayedUnchanged()
    {
        var reasoning = new TextReasoningContent("Considering record 555-867-5309");
        var call = new FunctionCallContent("call-1", "read_node", new Dictionary<string, object?> { ["ref"] = "observation/1" });
        var messages = new[] { new ChatMessage(ChatRole.Assistant, [reasoning, call]) };

        await Guard().GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        _provider.Calls.Single()[0].Contents.Should().Equal(reasoning, call);
    }

    /// <summary>A tenant that widens scrubbing to the HIPAA profile also loses dates.</summary>
    [Fact]
    public async Task HipaaProfile_AlsoScrubsDates()
    {
        await Guard(ScrubProfile.Hipaa).GetResponseAsync("Seen on 2026-03-14.", cancellationToken: TestContext.Current.CancellationToken);

        _provider.Calls.Single()[0].Text.Should().Be("Seen on [DATES].");
    }

    private OutboundGuardChatClient Guard(ScrubProfile profile = ScrubProfile.DirectIdentifiers) =>
        new(_provider.CreateClient("claude-opus-5-5"), new DeidDetector(), profile);
}
