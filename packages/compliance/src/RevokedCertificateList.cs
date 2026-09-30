using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Cadence.Compliance;

/// <summary>
/// The revocation list of an <see cref="ESignatureVerifier"/>, normalised as the Python reference
/// (<c>_get_normalized_revoked_certs</c>) does: a PEM entry revokes that exact PEM and its serial and SHA-256
/// fingerprint; an entry of 64 hex digits once colons, hyphens and spaces are removed is a SHA-256 fingerprint; a
/// decimal, <c>0x</c>-prefixed hex or plain hex entry is a serial number. A digits-only entry matches a serial read
/// either as decimal or as hex, because the Python keeps the entry text beside its decimal value. One addition to the
/// Python: an entry of 40 hex digits also matches the certificate's SHA-1 thumbprint.
/// </summary>
internal sealed class RevokedCertificateList
{
    private const string PemMarker = "-----BEGIN CERTIFICATE-----";

    private readonly HashSet<BigInteger> _serials = [];
    private readonly HashSet<string> _serialStrings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sha256Fingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sha1Thumbprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pems = new(StringComparer.Ordinal);

    public RevokedCertificateList(IEnumerable<string?>? entries)
    {
        foreach (var entry in entries ?? [])
        {
            Add(entry);
        }
    }

    private bool IsEmpty =>
        _serials.Count == 0 && _serialStrings.Count == 0 && _sha256Fingerprints.Count == 0 &&
        _sha1Thumbprints.Count == 0 && _pems.Count == 0;

    /// <summary>Whether the certificate, embedded in the document as <paramref name="certPem"/>, is revoked.</summary>
    public bool Contains(X509Certificate2 cert, string certPem)
    {
        if (IsEmpty)
        {
            return false;
        }

        var serial = SerialOf(cert);
        if (_serials.Contains(serial) || _serialStrings.Contains(serial.ToString(CultureInfo.InvariantCulture)))
        {
            return true;
        }

        if (serial.Sign >= 0)
        {
            var hex = ToHex(serial);
            if (_serialStrings.Contains(hex) || _serialStrings.Contains("0x" + hex))
            {
                return true;
            }
        }

        return _sha256Fingerprints.Contains(Sha256Fingerprint(cert))
            || _sha1Thumbprints.Contains(cert.Thumbprint.ToLowerInvariant())
            || _pems.Contains(certPem.Trim());
    }

    private void Add(string? entry)
    {
        var clean = entry?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return;
        }

        if (clean.Contains(PemMarker, StringComparison.Ordinal))
        {
            _pems.Add(clean);
            try
            {
                using var parsed = X509Certificate2.CreateFromPem(clean);
                AddSerial(SerialOf(parsed));
                _sha256Fingerprints.Add(Sha256Fingerprint(parsed));
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
            {
                // An unparsable PEM still revokes that exact text, as in the Python reference.
            }

            return;
        }

        var digest = clean
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (digest.Length == 64 && IsHex(digest))
        {
            _sha256Fingerprints.Add(digest);
        }
        else if (digest.Length == 40 && IsHex(digest))
        {
            _sha1Thumbprints.Add(digest);
        }

        if (clean.All(char.IsAsciiDigit))
        {
            AddSerial(BigInteger.Parse(clean, NumberStyles.None, CultureInfo.InvariantCulture));
            _serialStrings.Add(clean);
        }
        else if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHex(clean[2..], out var serial))
            {
                AddSerial(serial);
            }

            _serialStrings.Add(clean.ToLowerInvariant());
        }
        else
        {
            if (TryParseHex(clean, out var serial))
            {
                AddSerial(serial);
            }

            _serialStrings.Add(clean.ToLowerInvariant());
        }
    }

    private void AddSerial(BigInteger serial)
    {
        _serials.Add(serial);
        _serialStrings.Add(serial.ToString(CultureInfo.InvariantCulture));
        if (serial.Sign >= 0)
        {
            var hex = ToHex(serial);
            _serialStrings.Add(hex);
            _serialStrings.Add("0x" + hex);
        }
    }

    // The serial as the DER INTEGER it is encoded as, which is how the Python reads cert.serial_number.
    private static BigInteger SerialOf(X509Certificate2 cert) =>
        new(cert.SerialNumberBytes.Span, isUnsigned: false, isBigEndian: true);

    private static string Sha256Fingerprint(X509Certificate2 cert) =>
        Convert.ToHexStringLower(SHA256.HashData(cert.RawData));

    // Lower-case hex without leading zeros, like Python's hex(n)[2:].
    private static string ToHex(BigInteger value)
    {
        var hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static bool TryParseHex(string text, out BigInteger value)
    {
        value = BigInteger.Zero;
        return text.Length > 0
            && IsHex(text)
            && BigInteger.TryParse("0" + text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsHex(string text) => text.All(char.IsAsciiHexDigit);
}
