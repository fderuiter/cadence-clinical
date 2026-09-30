using Cadence.Security.Authentication;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The <c>X-Change-Reason</c> wire format (ADR-2495, #4895): printable ASCII travels as-is, anything else as
/// RFC 8187 <c>UTF-8''</c> percent-encoded UTF-8. The encoded strings match the web client's own tests, so
/// both sides agree byte for byte.
/// </summary>
public sealed class ChangeReasonHeaderTests
{
    private const string EmDashAndCjk = "IRB approval — 治験審査委員会の承認";

    private const string EmDashAndCjkEncoded =
        "UTF-8''IRB%20approval%20%E2%80%94%20%E6%B2%BB%E9%A8%93%E5%AF%A9%E6%9F%BB%E5%A7%94%E5%93%A1%E4%BC%9A%E3%81%AE%E6%89%BF%E8%AA%8D";

    [Theory]
    [InlineData("Corrected transcription error (5% off)")]
    [InlineData("")]
    public void Encode_PrintableAscii_IsUnchanged(string reason) =>
        ChangeReasonHeader.Encode(reason).Should().Be(reason);

    [Theory]
    [InlineData(EmDashAndCjk, EmDashAndCjkEncoded)]
    [InlineData("Réponse", "UTF-8''R%C3%A9ponse")]
    [InlineData("it’s (re)done*", "UTF-8''it%E2%80%99s%20%28re%29done%2A")]
    [InlineData("utf-8''abc", "UTF-8''utf-8%27%27abc")]
    [InlineData("line one\nline two", "UTF-8''line%20one%0Aline%20two")]
    public void Encode_AnythingElse_UsesTheRfc8187Form(string reason, string expected) =>
        ChangeReasonHeader.Encode(reason).Should().Be(expected);

    [Theory]
    [InlineData(EmDashAndCjk)]
    [InlineData("Réponse")]
    [InlineData("utf-8''abc")]
    [InlineData("Plain ASCII reason")]
    [InlineData("tab\tand\r\nnewline")]
    [InlineData("emoji 🧪 and Ελληνικά")]
    public void Decode_OfEncode_ReturnsExactlyWhatWasTyped(string reason)
    {
        ChangeReasonHeader.TryDecode(ChangeReasonHeader.Encode(reason), out string? decoded).Should().BeTrue();

        decoded.Should().Be(reason);
    }

    [Theory]
    [InlineData("utf-8''R%c3%a9ponse", "Réponse")]
    [InlineData("UTF-8'en'Reason%20text", "Reason text")]
    [InlineData("Fixed 5% error", "Fixed 5% error")]
    public void TryDecode_AcceptsLowercaseHexLanguageTagsAndPlainText(string header, string expected)
    {
        ChangeReasonHeader.TryDecode(header, out string? decoded).Should().BeTrue();

        decoded.Should().Be(expected);
    }

    [Theory]
    [InlineData("UTF-8''bad%2")]
    [InlineData("UTF-8''bad%ZZ")]
    [InlineData("UTF-8''space not encoded")]
    [InlineData("UTF-8''%C3%28")]
    [InlineData("UTF-8'no-closing-quote")]
    public void TryDecode_MalformedExtendedValue_IsRejected(string header) =>
        ChangeReasonHeader.TryDecode(header, out _).Should().BeFalse();
}
