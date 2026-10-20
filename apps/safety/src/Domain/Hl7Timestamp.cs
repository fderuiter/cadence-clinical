using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cadence.Apps.Safety.Domain;

/// <summary>The precision of an HL7 TS value, as the number of date and time digits it carries.</summary>
public enum Hl7TimestampPrecision
{
    /// <summary>CCYY.</summary>
    Year = 4,

    /// <summary>CCYYMM.</summary>
    Month = 6,

    /// <summary>CCYYMMDD.</summary>
    Day = 8,

    /// <summary>CCYYMMDDhh.</summary>
    Hour = 10,

    /// <summary>CCYYMMDDhhmm.</summary>
    Minute = 12,

    /// <summary>CCYYMMDDhhmmss.</summary>
    Second = 14,
}

/// <summary>
/// An HL7 v3 TS value as ICH E2B(R3) uses it (ICH ICSR Implementation Guide, Appendix II):
/// <c>CCYY[MM[DD[hh[mm[ss[.UUUU]]]]]][+/-ZZzz]</c>, where a time zone needs at least the hour.
/// </summary>
public static partial class Hl7Timestamp
{
    // The latest UTC offset in use; a value without a zone is read in it, so the earliest instant it can mean is used.
    private static readonly TimeSpan LatestOffset = TimeSpan.FromHours(14);

    [GeneratedRegex(@"^(?<digits>\d{4}(?:\d{2}(?:\d{2}(?:\d{2}(?:\d{2}(?:\d{2}(?:\.\d{1,4})?)?)?)?)?)?)(?<zone>[+-]\d{4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>
    /// Why <paramref name="value"/> is not a valid TS of at least <paramref name="minimum"/> precision that does not
    /// refer to a time after <paramref name="now"/>, or <see langword="null"/> when it is.
    /// </summary>
    public static string? Problem(string value, Hl7TimestampPrecision minimum, DateTimeOffset now)
    {
        if (!TryParse(value, out DateTimeOffset earliest, out Hl7TimestampPrecision precision))
        {
            return "is not an HL7 date/time (CCYY[MM[DD[hh[mm[ss]]]]][+/-ZZzz])";
        }

        if (precision < minimum)
        {
            return $"needs at least {minimum.ToString().ToLowerInvariant()} precision";
        }

        return earliest > now ? "refers to a future date" : null;
    }

    /// <summary>
    /// Parses <paramref name="value"/>, giving the earliest instant it can denote (the start of its precision, read in
    /// its own zone or, without one, in UTC+14:00) and its precision.
    /// </summary>
    public static bool TryParse(string? value, out DateTimeOffset earliest, out Hl7TimestampPrecision precision)
    {
        earliest = default;
        precision = default;
        var match = value is null ? null : Pattern().Match(value);
        if (match is not { Success: true })
        {
            return false;
        }

        string digits = match.Groups["digits"].Value;
        int dot = digits.IndexOf('.', StringComparison.Ordinal);
        string whole = dot < 0 ? digits : digits[..dot];
        precision = (Hl7TimestampPrecision)whole.Length;
        string zone = match.Groups["zone"].Value;
        if (zone.Length > 0 && precision < Hl7TimestampPrecision.Hour)
        {
            return false;
        }

        int Part(int start, int fallback) => whole.Length >= start + 2
            ? int.Parse(whole.AsSpan(start, 2), NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

        int year = int.Parse(whole.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture);
        int month = Part(4, 1);
        int day = Part(6, 1);
        int hour = Part(8, 0);
        int minute = Part(10, 0);
        int second = Part(12, 0);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        TimeSpan offset = LatestOffset;
        if (zone.Length > 0)
        {
            int zoneHours = int.Parse(zone.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture);
            int zoneMinutes = int.Parse(zone.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture);
            if (zoneHours > 14 || zoneMinutes > 59)
            {
                return false;
            }

            offset = new TimeSpan(zoneHours, zoneMinutes, 0);
            if (zone[0] == '-')
            {
                offset = offset.Negate();
            }
        }

        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        if (local - DateTime.MinValue < LatestOffset || DateTime.MaxValue - local < LatestOffset)
        {
            // Within 14 hours of the ends of the calendar the instant falls outside DateTimeOffset's range.
            return false;
        }

        earliest = new DateTimeOffset(local, offset);
        return true;
    }
}
