using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Authentication;

/// <summary>
/// The one encoding of the reason for change on the <c>X-Change-Reason</c> header (ADR-2495). A header value
/// must be ASCII, but a reason is free text in any script, so the value takes one of two forms:
/// <list type="bullet">
/// <item>plain printable ASCII, taken exactly as sent (<c>X-Change-Reason: Corrected transcription error</c>);</item>
/// <item>the RFC 8187 extended form, <c>UTF-8''</c> followed by the percent-encoded UTF-8 bytes of the text
/// (<c>X-Change-Reason: UTF-8''IRB%20approval%20%E2%80%94%20signed</c>).</item>
/// </list>
/// Every writer encodes with <see cref="Encode"/> (or the web client's equivalent) and every reader decodes
/// with <see cref="TryDecode"/> or <see cref="Read"/>, so the audit trail stores exactly what the user typed.
/// </summary>
public static class ChangeReasonHeader
{
    /// <summary>The prefix that marks the RFC 8187 extended form. The charset name is case-insensitive.</summary>
    public const string ExtendedPrefix = "UTF-8''";

    private const string HexDigits = "0123456789ABCDEF";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes <paramref name="reason"/> for the header: unchanged when it is printable ASCII that cannot be
    /// mistaken for the extended form, otherwise <c>UTF-8''</c> plus its percent-encoded UTF-8 bytes.
    /// </summary>
    public static string Encode(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (IsPlain(reason))
        {
            return reason;
        }

        byte[] bytes = StrictUtf8.GetBytes(reason);
        var builder = new StringBuilder(ExtendedPrefix, ExtendedPrefix.Length + (bytes.Length * 3));
        foreach (byte b in bytes)
        {
            if (IsAttrChar(b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(HexDigits[b >> 4]).Append(HexDigits[b & 0xF]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a header value into the reason text. A value in the extended form must be well-formed: every
    /// <c>%</c> starts a two-digit hex escape, every other character is visible ASCII, and the bytes are valid
    /// UTF-8. Any other value is plain text and is returned unchanged. Returns <see langword="false"/> only for
    /// a malformed extended value.
    /// </summary>
    public static bool TryDecode(string? headerValue, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        if (headerValue is null)
        {
            return false;
        }

        if (!headerValue.StartsWith("UTF-8'", StringComparison.OrdinalIgnoreCase))
        {
            reason = headerValue;
            return true;
        }

        // RFC 8187: charset "'" [ language ] "'" value-chars. The language tag carries nothing for an audit
        // entry, so it is skipped.
        int languageEnd = headerValue.IndexOf('\'', "UTF-8'".Length);
        if (languageEnd < 0)
        {
            return false;
        }

        ReadOnlySpan<char> encoded = headerValue.AsSpan(languageEnd + 1);
        byte[] bytes = new byte[encoded.Length];
        int count = 0;
        int i = 0;
        while (i < encoded.Length)
        {
            char c = encoded[i];
            if (c == '%')
            {
                if (i + 2 >= encoded.Length)
                {
                    return false;
                }

                int high = HexValue(encoded[i + 1]);
                int low = HexValue(encoded[i + 2]);
                if (high < 0 || low < 0)
                {
                    return false;
                }

                bytes[count++] = (byte)((high << 4) | low);
                i += 3;
            }
            else if (c is > ' ' and < '\u007F')
            {
                bytes[count++] = (byte)c;
                i++;
            }
            else
            {
                return false;
            }
        }

        try
        {
            reason = StrictUtf8.GetString(bytes, 0, count);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// The decoded reason on <paramref name="request"/>, or <see langword="null"/> when the header is absent,
    /// blank or malformed.
    /// </summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string value = request.Headers[GatewayHeaders.ChangeReason].ToString();
        return TryDecode(value, out string? reason) && !string.IsNullOrWhiteSpace(reason) ? reason : null;
    }

    private static bool IsPlain(string reason) =>
        reason.All(c => c is >= ' ' and <= '~') && !reason.StartsWith("UTF-8'", StringComparison.OrdinalIgnoreCase);

    // RFC 8187 attr-char: ALPHA / DIGIT / "!" / "#" / "$" / "&" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
    private static bool IsAttrChar(byte b) =>
        b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
            or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'&' or (byte)'+' or (byte)'-' or (byte)'.'
            or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~';

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
