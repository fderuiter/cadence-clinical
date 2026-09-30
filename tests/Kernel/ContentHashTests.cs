using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Trust;
using Cadence.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The canonical content hash attestations bind to (spec §9.5, 21 CFR 11.70): stable across serialisations of the
/// same document and different for any change to it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ContentHashTests
{
    private const string Document = """{"study":{"name":"CARDIO-1","arms":["A","B"],"phase":3,"blinded":true,"notes":null}}""";

    /// <summary>Key order, whitespace and escaping do not change the hash.</summary>
    [Theory(DisplayName = "Compute_IsStableAcrossSerialisations")]
    [InlineData("""{ "study" : { "phase": 3, "notes": null, "blinded": true, "arms": [ "A", "B" ], "name": "CARDIO-1" } }""")]
    [InlineData("{\n  \"study\": {\n    \"arms\": [\"A\", \"B\"],\n    \"blinded\": true,\n    \"name\": \"CARDIO-1\",\n    \"notes\": null,\n    \"phase\": 3\n  }\n}")]
    [InlineData("""{"study":{"name":"CARDIO-1","arms":["A","B"],"phase":3,"blinded":true,"notes":null}}""")]
    public void ComputeIsStableAcrossSerialisations(string equivalent)
    {
        ContentHash.Compute(equivalent).Should().Be(ContentHash.Compute(Document));
    }

    /// <summary>Any change to a value, a key, the array order or the number's written form changes the hash.</summary>
    [Theory(DisplayName = "Compute_ChangesWithTheContent")]
    [InlineData("""{"study":{"name":"CARDIO-2","arms":["A","B"],"phase":3,"blinded":true,"notes":null}}""")]
    [InlineData("""{"study":{"name":"CARDIO-1","arms":["B","A"],"phase":3,"blinded":true,"notes":null}}""")]
    [InlineData("""{"study":{"title":"CARDIO-1","arms":["A","B"],"phase":3,"blinded":true,"notes":null}}""")]
    [InlineData("""{"study":{"name":"CARDIO-1","arms":["A","B"],"phase":3.0,"blinded":true,"notes":null}}""")]
    [InlineData("""{"study":{"name":"CARDIO-1","arms":["A","B"],"phase":3,"blinded":true}}""")]
    public void ComputeChangesWithTheContent(string changed)
    {
        ContentHash.Compute(changed).Should().NotBe(ContentHash.Compute(Document));
    }

    /// <summary>
    /// The canonical form is the one <c>CanonicalSigner</c> uses for signed payloads, so the kernel and the security
    /// package agree on what "the same content" means.
    /// </summary>
    [Fact(DisplayName = "Canonicalize_MatchesCanonicalSigner")]
    public void CanonicalizeMatchesCanonicalSigner()
    {
        const string json = """{"z":"é <&>","a":[{"y":1,"b":2}],"m":{"k":"é"}}""";

        ContentHash.Canonicalize(json).Should().Be(Encoding.UTF8.GetString(CanonicalSigner.CanonicalSerialize(json)));
        ContentHash.Canonicalize(json).Should().Be("""{"a":[{"b":2,"y":1}],"m":{"k":"é"},"z":"é <&>"}""");
    }

    /// <summary>The hash is <c>sha256:</c> and lower-case hex SHA-256, and the shape check accepts only that.</summary>
    [Fact(DisplayName = "Compute_IsPrefixedLowerCaseHexSha256")]
    public void ComputeIsPrefixedLowerCaseHexSha256()
    {
        var hash = ContentHash.Compute(Document);

        hash.Should().MatchRegex(ContentHash.Pattern).And.HaveLength(ContentHash.Length);
        hash.Should().Be(ContentHash.Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ContentHash.Canonicalize(Document)))));
        ContentHash.IsWellFormed(hash).Should().BeTrue();
        ContentHash.IsWellFormed(hash.ToUpperInvariant()).Should().BeFalse();
        ContentHash.IsWellFormed(hash[..^1]).Should().BeFalse();
        ContentHash.IsWellFormed(hash[ContentHash.Prefix.Length..]).Should().BeFalse();
        ContentHash.IsWellFormed("sha512:" + hash[ContentHash.Prefix.Length..]).Should().BeFalse();
        ContentHash.IsWellFormed(hash[..^1] + "g").Should().BeFalse();
        ContentHash.IsWellFormed(null).Should().BeFalse();
    }

    /// <summary>
    /// Governed standards metadata (<see cref="CanonicalJson"/>) canonicalizes and hashes exactly as the kernel content
    /// hash does, so one signature scheme covers protocol versions, standard versions and library items.
    /// </summary>
    [Fact(DisplayName = "CanonicalJson_MatchesTheKernelContentHash")]
    public void CanonicalJsonMatchesTheKernelContentHash()
    {
        const string json = """{"z":"é <&>\u0041\n","a":[{"y":1.50,"b":-2e3}],"m":{"k":null,"t":true}}""";
        using var document = JsonDocument.Parse(json);

        CanonicalJson.Serialize(document.RootElement).Should().Be(ContentHash.Canonicalize(json));
        CanonicalJson.Hash(document.RootElement).Should().Be(ContentHash.Compute(json));
    }
}
