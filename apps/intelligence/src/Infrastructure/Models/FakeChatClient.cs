using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Infrastructure.Models;

/// <summary>
/// The deterministic fake provider (ADR-2630 §4.2): the same messages always get the same answer and usage, so tests and
/// CI never call a model. By default it answers with a fingerprint of the conversation; tests pass a responder to script
/// answers. Its provider name is <see cref="ProviderName"/>, and its answers say they are fake.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    /// <summary>The provider name task configurations use for the fake.</summary>
    public const string ProviderName = "fake";

    private readonly Func<IReadOnlyList<ChatMessage>, ChatOptions?, string> _responder;

    /// <summary>A fake that answers with <paramref name="responder"/>, or with a fingerprint of the conversation.</summary>
    public FakeChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, string>? responder = null)
    {
        _responder = responder ?? DefaultAnswer;
    }

    /// <summary>How many calls the fake has answered.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        var text = _responder(list, options);
        Calls++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = options?.ModelId,
            Usage = new UsageDetails
            {
                InputTokenCount = Tokens(string.Concat(list.Select(m => m.Text))),
                OutputTokenCount = Tokens(text),
            },
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata(ProviderName);
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Holds nothing.
    }

    /// <summary>A stand-in token count: one token per four characters, rounded up.</summary>
    internal static long Tokens(string text) => (text.Length + 3) / 4;

    private static string DefaultAnswer(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", messages.Select(m => $"{m.Role.Value}:{m.Text}")))))[..12];
        return $"[fake {options?.ModelId ?? "model"}] {fingerprint}";
    }
}
