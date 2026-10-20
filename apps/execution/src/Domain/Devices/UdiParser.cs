using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Apps.Execution.Domain.Devices;

/// <summary>
/// Parsed representation of a Unique Device Identifier (UDI) barcode string.
/// </summary>
public record ParsedUdiResult(
    string RawBarcode,
    string UdiCarrierFormat,
    string UdiDi,
    string? LotNumber,
    string? SerialNumber,
    string? ExpirationDate,
    string? ManufacturingDate,
    string? SoftwareVersion,
    bool IsValid,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Stateless engine for decoding GS1-128, GS1-DataMatrix, HIBCC-128, and HIBCC-DataMatrix UDI barcode payloads.
/// Validates GTIN Mod-10 and HIBCC Mod-43 checksums per FDA UDI Rule, EU MDR/IVDR, and ISO 14155:2020.
/// @req:PRD-DEV-001
/// </summary>
public static class UdiParser
{
    private static readonly string[] EmptyBarcodeErrors = ["Barcode string cannot be null or empty."];

    private static readonly char[] HibccMod43Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%".ToCharArray();

    /// <summary>
    /// Parses a raw UDI barcode string into structured components.
    /// </summary>
    public static ParsedUdiResult Parse(string rawBarcode)
    {
        if (string.IsNullOrWhiteSpace(rawBarcode))
        {
            return new ParsedUdiResult(
                RawBarcode: rawBarcode ?? string.Empty,
                UdiCarrierFormat: "RAW_UDI",
                UdiDi: string.Empty,
                LotNumber: null,
                SerialNumber: null,
                ExpirationDate: null,
                ManufacturingDate: null,
                SoftwareVersion: null,
                IsValid: false,
                ValidationErrors: EmptyBarcodeErrors);
        }

        var trimmed = rawBarcode.Trim();
        var errors = new List<string>();

        // Check format
        if (trimmed.StartsWith('+') || trimmed.StartsWith("]d2+", StringComparison.Ordinal) || trimmed.StartsWith("]C0+", StringComparison.Ordinal))
        {
            return ParseHibcc(trimmed);
        }

        if (trimmed.Contains("(01)") || trimmed.StartsWith("01", StringComparison.Ordinal) || trimmed.StartsWith("]C1", StringComparison.Ordinal) || trimmed.StartsWith("]d2", StringComparison.Ordinal))
        {
            return ParseGs1(trimmed);
        }

        // Fallback for raw GTIN or simple string
        if (Regex.IsMatch(trimmed, @"^\d{14}$"))
        {
            bool validGtin = ValidateGtinMod10(trimmed);
            if (!validGtin)
            {
                errors.Add("GTIN-14 Modulo 10 check digit verification failed.");
            }

            return new ParsedUdiResult(
                RawBarcode: trimmed,
                UdiCarrierFormat: "GS1_128",
                UdiDi: trimmed,
                LotNumber: null,
                SerialNumber: null,
                ExpirationDate: null,
                ManufacturingDate: null,
                SoftwareVersion: null,
                IsValid: validGtin,
                ValidationErrors: errors);
        }

        // Raw fallback
        return new ParsedUdiResult(
            RawBarcode: trimmed,
            UdiCarrierFormat: "RAW_UDI",
            UdiDi: trimmed,
            LotNumber: null,
            SerialNumber: null,
            ExpirationDate: null,
            ManufacturingDate: null,
            SoftwareVersion: null,
            IsValid: true,
            ValidationErrors: Array.Empty<string>());
    }

    private static ParsedUdiResult ParseGs1(string barcode)
    {
        var errors = new List<string>();
        string clean = barcode;

        // Strip DataMatrix prefix if present
        if (clean.StartsWith("]C1", StringComparison.Ordinal) || clean.StartsWith("]d2", StringComparison.Ordinal))
        {
            clean = clean.Substring(3);
        }

        string carrierFormat = barcode.Contains("]d2") || barcode.Contains("DataMatrix") ? "GS1_DATAMATRIX" : "GS1_128";

        string udiDi = string.Empty;
        string? lotNumber = null;
        string? serialNumber = null;
        string? expirationDate = null;
        string? manufacturingDate = null;
        string? softwareVersion = null;

        // If barcode has parenthesized AIs like (01)00850006789012(17)261231(10)LOT123(21)SN987
        if (clean.Contains("(01)"))
        {
            var matches = Regex.Matches(clean, @"\((?<ai>\d{2,4})\)(?<val>[^\(\)]+)");
            for (int m = 0; m < matches.Count; m++)
            {
                Match match = matches[m];
                string ai = match.Groups["ai"].Value;
                string val = match.Groups["val"].Value.Trim();

                switch (ai)
                {
                    case "01":
                        udiDi = val;
                        break;
                    case "10":
                        lotNumber = val;
                        break;
                    case "11":
                        manufacturingDate = FormatGs1Date(val);
                        break;
                    case "17":
                        expirationDate = FormatGs1Date(val);
                        break;
                    case "21":
                        serialNumber = val;
                        break;
                    case "8012":
                        softwareVersion = val;
                        break;
                }
            }
        }
        else
        {
            // Sequential parse without parentheses (e.g. 01008500067890121726123110LOT123\x1d21SN987)
            int index = 0;
            while (index < clean.Length)
            {
                // Handle FNC1 / GS separator
                if (clean[index] == '\x1d' || clean[index] == '\u001d')
                {
                    index++;
                    continue;
                }

                if (index + 2 <= clean.Length && clean.Substring(index, 2) == "01" && index + 16 <= clean.Length)
                {
                    udiDi = clean.Substring(index + 2, 14);
                    index += 16;
                }
                else if (index + 2 <= clean.Length && clean.Substring(index, 2) == "17" && index + 8 <= clean.Length)
                {
                    expirationDate = FormatGs1Date(clean.Substring(index + 2, 6));
                    index += 8;
                }
                else if (index + 2 <= clean.Length && clean.Substring(index, 2) == "11" && index + 8 <= clean.Length)
                {
                    manufacturingDate = FormatGs1Date(clean.Substring(index + 2, 6));
                    index += 8;
                }
                else if (index + 2 <= clean.Length && clean.Substring(index, 2) == "10")
                {
                    index += 2;
                    int end = clean.IndexOf('\u001d', index);
                    if (end == -1)
                    {
                        // Check if 21 or 17 or 11 AI starts
                        int next21 = clean.IndexOf("21", index, StringComparison.Ordinal);
                        int next17 = clean.IndexOf("17", index, StringComparison.Ordinal);
                        int next11 = clean.IndexOf("11", index, StringComparison.Ordinal);
                        int nextAi = Math.Min(next21 == -1 ? int.MaxValue : next21, Math.Min(next17 == -1 ? int.MaxValue : next17, next11 == -1 ? int.MaxValue : next11));
                        end = nextAi == int.MaxValue ? clean.Length : nextAi;
                    }
                    lotNumber = clean.Substring(index, end - index);
                    index = end;
                }
                else if (index + 2 <= clean.Length && clean.Substring(index, 2) == "21")
                {
                    index += 2;
                    int end = clean.IndexOf('\u001d', index);
                    if (end == -1) end = clean.Length;
                    serialNumber = clean.Substring(index, end - index);
                    index = end;
                }
                else if (index + 4 <= clean.Length && clean.Substring(index, 4) == "8012")
                {
                    index += 4;
                    int end = clean.IndexOf('\u001d', index);
                    if (end == -1) end = clean.Length;
                    softwareVersion = clean.Substring(index, end - index);
                    index = end;
                }
                else
                {
                    index++;
                }
            }
        }

        if (string.IsNullOrEmpty(udiDi))
        {
            errors.Add("GS1 barcode missing required AI (01) GTIN / Device Identifier.");
        }
        else if (udiDi.Length == 14 && !ValidateGtinMod10(udiDi))
        {
            errors.Add("GTIN-14 Modulo 10 check digit verification failed.");
        }

        return new ParsedUdiResult(
            RawBarcode: barcode,
            UdiCarrierFormat: carrierFormat,
            UdiDi: udiDi,
            LotNumber: lotNumber,
            SerialNumber: serialNumber,
            ExpirationDate: expirationDate,
            ManufacturingDate: manufacturingDate,
            SoftwareVersion: softwareVersion,
            IsValid: errors.Count == 0,
            ValidationErrors: errors);
    }

    private static ParsedUdiResult ParseHibcc(string barcode)
    {
        var errors = new List<string>();
        string clean = barcode;

        if (clean.StartsWith("]d2", StringComparison.Ordinal) || clean.StartsWith("]C0", StringComparison.Ordinal) || clean.StartsWith("]C1", StringComparison.Ordinal))
        {
            clean = clean.Substring(3);
        }

        string carrierFormat = barcode.Contains("DataMatrix") || barcode.StartsWith("]d2", StringComparison.Ordinal) || barcode.StartsWith("]C0", StringComparison.Ordinal) ? "HIBCC_DATAMATRIX" : "HIBCC_128";

        // Example HIBCC: +H12345678901/$$32601011234/5B or +H12345678901/$3260101LOT123
        string primary = clean;
        string secondary = string.Empty;

        int slashPos = clean.IndexOf('/');
        if (slashPos != -1)
        {
            primary = clean.Substring(0, slashPos);
            secondary = clean.Substring(slashPos + 1);
        }
        else
        {
            int secPos = clean.IndexOf("$$", 1, StringComparison.Ordinal);
            if (secPos == -1) secPos = clean.IndexOf("+$", 1, StringComparison.Ordinal);
            if (secPos != -1)
            {
                primary = clean.Substring(0, secPos);
                secondary = clean.Substring(secPos);
            }
        }

        // Validate primary HIBCC format: starts with +, followed by LIC (1 letter + 3 alpha/num) and product code
        string udiDi = primary;
        if (primary.StartsWith('+') && primary.Length >= 5)
        {
            // Remove '+' and check character if present
            udiDi = primary.Substring(1);
            if (!IsHibccMod43Valid(primary))
            {
                errors.Add("HIBCC Modulo 43 check character verification failed.");
            }
        }
        else
        {
            errors.Add("Invalid HIBCC primary barcode structure.");
        }

        string? lotNumber = null;
        string? serialNumber = null;
        string? expirationDate = null;

        if (!string.IsNullOrEmpty(secondary))
        {
            bool hasMod43 = IsHibccMod43Valid(secondary) || IsHibccMod43Valid("+" + secondary);

            if (secondary.StartsWith("$$7", StringComparison.Ordinal) || secondary.StartsWith("$7", StringComparison.Ordinal))
            {
                int start = secondary.StartsWith("$$7", StringComparison.Ordinal) ? 3 : 2;
                string rem = secondary.Substring(start);
                serialNumber = hasMod43 && rem.Length > 1 ? rem.Substring(0, rem.Length - 1) : rem;
            }
            else if (secondary.StartsWith("$$3", StringComparison.Ordinal) || secondary.StartsWith("$3", StringComparison.Ordinal))
            {
                int dateStart = secondary.StartsWith("$$3", StringComparison.Ordinal) ? 3 : 2;
                if (secondary.Length >= dateStart + 6)
                {
                    string yymmdd = secondary.Substring(dateStart, 6);
                    expirationDate = FormatGs1Date(yymmdd);
                    string remainder = secondary.Substring(dateStart + 6);
                    if (!string.IsNullOrEmpty(remainder))
                    {
                        if (remainder.StartsWith("/S", StringComparison.OrdinalIgnoreCase) || remainder.StartsWith("16S", StringComparison.OrdinalIgnoreCase))
                        {
                            int sIndex = remainder.IndexOf('S') + 1;
                            string rem = remainder.Substring(sIndex);
                            serialNumber = hasMod43 && rem.Length > 1 ? rem.Substring(0, rem.Length - 1) : rem;
                        }
                        else
                        {
                            lotNumber = hasMod43 && remainder.Length > 1 ? remainder.Substring(0, remainder.Length - 1) : remainder;
                        }
                    }
                }
            }
            else if (secondary.StartsWith("+$", StringComparison.Ordinal) || secondary.StartsWith('$'))
            {
                int start = secondary.StartsWith("+$", StringComparison.Ordinal) ? 2 : 1;
                string remainder = secondary.Substring(start);
                lotNumber = hasMod43 && remainder.Length > 1 ? remainder.Substring(0, remainder.Length - 1) : remainder;
            }
            else
            {
                lotNumber = hasMod43 && secondary.Length > 1 ? secondary.Substring(0, secondary.Length - 1) : secondary;
            }
        }

        return new ParsedUdiResult(
            RawBarcode: barcode,
            UdiCarrierFormat: carrierFormat,
            UdiDi: udiDi,
            LotNumber: lotNumber,
            SerialNumber: serialNumber,
            ExpirationDate: expirationDate,
            ManufacturingDate: null,
            SoftwareVersion: null,
            IsValid: errors.Count == 0,
            ValidationErrors: errors);
    }

    /// <summary>
    /// Validates GTIN-14 Modulo 10 checksum.
    /// </summary>
    public static bool ValidateGtinMod10(string gtin)
    {
        if (string.IsNullOrWhiteSpace(gtin) || gtin.Length != 14 || !ulong.TryParse(gtin, out _))
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            int digit = gtin[i] - '0';
            sum += (i % 2 == 0) ? digit * 3 : digit * 1;
        }

        int checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == (gtin[13] - '0');
    }

    /// <summary>
    /// Validates HIBCC Modulo 43 check character.
    /// </summary>
    public static bool IsHibccMod43Valid(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length < 2)
        {
            return false;
        }

        int sum = 0;
        string payload = code.Substring(0, code.Length - 1);
        char expectedCheck = code[code.Length - 1];

        foreach (char c in payload)
        {
            int index = Array.IndexOf(HibccMod43Chars, char.ToUpperInvariant(c));
            if (index == -1) return false;
            sum += index;
        }

        int checkIndex = sum % 43;
        return HibccMod43Chars[checkIndex] == char.ToUpperInvariant(expectedCheck);
    }

    private static string? FormatGs1Date(string yymmdd)
    {
        if (yymmdd.Length != 6 || !int.TryParse(yymmdd, out _))
        {
            return yymmdd;
        }

        int yy = int.Parse(yymmdd.AsSpan(0, 2), CultureInfo.InvariantCulture);
        int mm = int.Parse(yymmdd.AsSpan(2, 2), CultureInfo.InvariantCulture);
        int dd = int.Parse(yymmdd.AsSpan(4, 2), CultureInfo.InvariantCulture);

        // Standard GS1 rule: DD = 00 means last day of the month
        int year = 2000 + yy;
        if (mm < 1 || mm > 12) return yymmdd;

        if (dd == 0)
        {
            dd = DateTime.DaysInMonth(year, mm);
        }

        return new DateTime(year, mm, dd, 23, 59, 59, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
