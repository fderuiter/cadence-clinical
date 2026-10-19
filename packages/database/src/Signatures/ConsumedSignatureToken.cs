using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Database.Signatures;

/// <summary>
/// One used e-signature step-up token, keyed by its <c>jti</c>. The primary key makes "first use wins" a single
/// atomic insert, shared by every instance of the host and kept across restarts (ADR-2515). A row is needed only
/// until <see cref="RetainUntil"/>, the end of the token's acceptance window; after that the verifier refuses the
/// token on its timestamps alone. Every service that maps an e-signature route keeps this table in its own schema
/// (#5434).
/// </summary>
public sealed class ConsumedSignatureToken
{
    /// <summary>The token's <c>jti</c> claim.</summary>
    public string TokenId { get; init; } = string.Empty;

    /// <summary>When the token was used.</summary>
    public DateTimeOffset ConsumedAt { get; init; }

    /// <summary>The end of the token's acceptance window (<c>exp</c> plus the verifier's clock skew).</summary>
    public DateTimeOffset RetainUntil { get; init; }
}

/// <summary>EF Core configuration for <see cref="ConsumedSignatureToken"/>.</summary>
public sealed class ConsumedSignatureTokenConfiguration : IEntityTypeConfiguration<ConsumedSignatureToken>
{
    /// <summary>The token id length limit; the gateway issues GUIDs.</summary>
    public const int MaxTokenIdLength = 128;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConsumedSignatureToken> builder)
    {
        builder.ToTable("consumed_signature_tokens");
        builder.HasKey(t => t.TokenId);
        builder.Property(t => t.TokenId).HasMaxLength(MaxTokenIdLength);
        builder.Property(t => t.ConsumedAt).IsRequired();
        builder.Property(t => t.RetainUntil).IsRequired();
        builder.HasIndex(t => t.RetainUntil);
    }
}
