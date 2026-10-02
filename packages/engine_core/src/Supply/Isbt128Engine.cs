using System;
using System.Linq;

namespace Cadence.Core.Domain.Supply;

public static class Isbt128Engine
{
    private const string Iso7064Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ*";

    public static char CalculateIso7064Mod37_2(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return '0';
        }

        string clean = data.ToUpperInvariant().Replace("=", "").Replace(" ", "").Replace("-", "");
        int p = 36;
        foreach (char ch in clean)
        {
            int val = Iso7064Charset.IndexOf(ch);
            if (val == -1)
            {
                val = 0;
            }

            int s = (p + val) % 36;
            if (s == 0)
            {
                s = 36;
            }

            p = (s * 2) % 37;
        }

        int checkVal = (38 - p) % 37;
        return checkVal < Iso7064Charset.Length ? Iso7064Charset[checkVal] : '0';
    }

    public static string GenerateDin(
        string facilityCode = "W1234",
        int? year = null,
        int? sequenceNum = null,
        int defaultYear = 26)
    {
        string fac = (facilityCode ?? "W1234").ToUpperInvariant().Replace("=", "").Trim();
        if (fac.Length < 5)
        {
            fac = fac.PadRight(5, '0');
        }
        else if (fac.Length > 5)
        {
            fac = fac[..5];
        }

        int currentYear = year is >= 0 and <= 99 ? year.Value : defaultYear;
        int seq = sequenceNum is > 0 ? sequenceNum.Value : (Random.Shared.Next(100000, 999999)); // deid-ignore
        string seqStr = (seq % 1000000).ToString("D6"); // deid-ignore

        string coreDin = $"{fac}{currentYear:D2}{seqStr}";
        char chk1 = CalculateIso7064Mod37_2(coreDin);
        char chk2 = CalculateIso7064Mod37_2($"{coreDin}{chk1}");

        return $"{coreDin}{chk1}{chk2}";
    }

    public static bool ValidateDin(string din)
    {
        if (string.IsNullOrWhiteSpace(din))
        {
            return false;
        }

        string clean = din.ToUpperInvariant().Replace("=", "").Replace(" ", "").Replace("-", "").Trim();
        if (clean.Length < 13)
        {
            return false;
        }

        if (clean.Length >= 15)
        {
            string coreDin = clean[..13];
            char expectedChk1 = CalculateIso7064Mod37_2(coreDin);
            char expectedChk2 = CalculateIso7064Mod37_2($"{coreDin}{expectedChk1}");
            string expectedChk = $"{expectedChk1}{expectedChk2}";
            string actualChk = clean.Substring(13, 2);

            return string.Equals(actualChk, expectedChk, StringComparison.OrdinalIgnoreCase);
        }

        return clean.All(char.IsLetterOrDigit);
    }
}
