using System;
using System.Text.Json;

using Cadence.Kernel.Trust;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// <see cref="JsonCanonicalization"/> against the examples in RFC 8785, so a hash the server computes matches one a
/// client computes with <c>JSON.stringify</c>-style canonicalization (#5023).
/// </summary>
public sealed class JsonCanonicalizationTests
{
    /// <summary>RFC 8785 appendix B: IEEE 754 doubles and their ECMAScript text.</summary>
    [Theory]
    [InlineData(0x0000000000000000UL, "0")]
    [InlineData(0x8000000000000000UL, "0")]
    [InlineData(0x0000000000000001UL, "5e-324")]
    [InlineData(0x8000000000000001UL, "-5e-324")]
    [InlineData(0x7fefffffffffffffUL, "1.7976931348623157e+308")]
    [InlineData(0x4340000000000000UL, "9007199254740992")]
    [InlineData(0xc340000000000000UL, "-9007199254740992")]
    [InlineData(0x4430000000000000UL, "295147905179352830000")]
    [InlineData(0x44b52d02c7e14af5UL, "9.999999999999997e+22")]
    [InlineData(0x44b52d02c7e14af6UL, "1e+23")]
    [InlineData(0x44b52d02c7e14af7UL, "1.0000000000000001e+23")]
    [InlineData(0x444b1ae4d6e2ef4eUL, "999999999999999700000")]
    [InlineData(0x444b1ae4d6e2ef4fUL, "999999999999999900000")]
    [InlineData(0x444b1ae4d6e2ef50UL, "1e+21")]
    [InlineData(0x3eb0c6f7a0b5ed8cUL, "9.999999999999997e-7")]
    [InlineData(0x3eb0c6f7a0b5ed8dUL, "0.000001")]
    [InlineData(0x41b3de4355555553UL, "333333333.3333332")]
    [InlineData(0x41b3de4355555554UL, "333333333.33333325")]
    [InlineData(0x41b3de4355555555UL, "333333333.3333333")]
    [InlineData(0x41b3de4355555556UL, "333333333.3333334")]
    [InlineData(0x41b3de4355555557UL, "333333333.33333343")]
    [InlineData(0xbecbf647612f3696UL, "-0.0000033333333333333333")]
    [InlineData(0x43143ff3c1cb0959UL, "1424953923781206.2")]
    public void FormatNumber_WritesTheRfc8785Text(ulong bits, string expected)
    {
        JsonCanonicalization.FormatNumber(BitConverter.UInt64BitsToDouble(bits)).Should().Be(expected);
    }

    /// <summary>RFC 8785 §3.2.4: sorted members, normalized numbers and minimal string escapes.</summary>
    [Fact]
    public void Canonicalize_MatchesTheRfc8785Example()
    {
        const string input = """
            {
              "numbers": [333333333.33333329, 1E30, 4.50, 2e-3, 0.000000000000000000000000001],
              "string": "\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"\/",
              "literals": [null, true, false]
            }
            """;

        JsonCanonicalization.Canonicalize(input).Should().Be(
            """{"literals":[null,true,false],"numbers":[333333333.3333333,1e+30,4.5,0.002,1e-27],"string":"€$\u000f\nA'B\"\\\\\"/"}""");
    }

    /// <summary>RFC 8785 §3.2.3: members sort by UTF-16 code units, at every depth.</summary>
    [Fact]
    public void Canonicalize_SortsMembersByUtf16CodeUnits()
    {
        const string input = """{"\u20ac":1,"\r":2,"\ufb33":3,"1":4,"\ud83d\ude00":5,"\u0080":6,"\u00f6":7,"n":{"b":1,"a":[{"d":1,"c":2}]}}""";

        JsonCanonicalization.Canonicalize(input).Should().Be(
            "{\"\\r\":2,\"1\":4,\"n\":{\"a\":[{\"c\":2,\"d\":1}],\"b\":1},\"\u0080\":6,\"ö\":7,\"€\":1,\"😀\":5,\"\ufb33\":3}");
    }

    /// <summary>
    /// The text a client submits and the text PostgreSQL returns for the same <c>jsonb</c> value hash the same.
    /// </summary>
    [Fact]
    public void Sha256Hex_IsTheSameForTheSubmittedAndTheJsonbText()
    {
        const string submitted = "{ \"vssbp\" : 1.2e2,\n  \"observation_date\":\"2026-01-01T08:00:00Z\", \"unit\": \"mmHg\" }";
        const string fromJsonb = "{\"unit\": \"mmHg\", \"vssbp\": 120, \"observation_date\": \"2026-01-01T08:00:00Z\"}";

        JsonCanonicalization.Sha256Hex(submitted).Should().Be(JsonCanonicalization.Sha256Hex(fromJsonb))
            .And.MatchRegex("^[0-9a-f]{64}$");
        JsonCanonicalization.Sha256Hex("""{"vssbp":121}""").Should().NotBe(JsonCanonicalization.Sha256Hex("""{"vssbp":120}"""));
    }

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"a":1e400}""")]
    [InlineData("not json")]
    [InlineData("""{"a":1} {"b":2}""")]
    public void Canonicalize_RefusesTextJcsCannotRepresent(string input)
    {
        var act = () => JsonCanonicalization.Canonicalize(input);

        act.Should().Throw<JsonException>();
    }
}
