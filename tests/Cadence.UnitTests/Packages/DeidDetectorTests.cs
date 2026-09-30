using System.Collections.Generic;
using System.Linq;

using Cadence.Deid;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Parity tests for <see cref="DeidDetector"/> and <see cref="ProfileCategories"/> against the Python reference
/// (<c>packages/deid/detector.py</c> and <c>models.py</c>, deleted once these tests landed and kept in git history). Every expected detection and redaction below is the
/// output of the Python detector for the same input. All identifiers are synthetic: documentation IP ranges
/// (192.0.2.0/24, 2001:db8::/32), fictional 555-01xx telephone numbers, an invalid 000- SSN and made-up codes.
/// </summary>
public class DeidDetectorTests
{
    private static readonly string[] EuCtrExcludedCategories =
    [
        DetectorCategory.ZipGeographic,
        DetectorCategory.Urls,
        DetectorCategory.IpMacAddresses,
        DetectorCategory.HealthPlanBeneficiary,
        DetectorCategory.CertificateLicense,
        DetectorCategory.VehicleIdentifiers,
        DetectorCategory.DeviceSerial,
    ];

    /// <summary>
    /// Detects each of the 14 categories, resolves overlaps and redacts exactly as the Python reference does,
    /// including the profile filter (EU_CTR drops geographic, URL, network and non-patient identifiers).
    /// </summary>
    [Theory]
    // email, telephone_fax, ssn_national_id, dates
    [InlineData("Contact test.user@example.org for details.", ComplianceProfile.HIPAA, null, "email 8-29 'test.user@example.org'", "Contact [EMAIL] for details.")]
    [InlineData("Call 555-010-0199 today.", ComplianceProfile.HIPAA, null, "telephone_fax 5-17 '555-010-0199'", "Call [TELEPHONE_FAX] today.")]
    [InlineData("SSN 000-12-3456 on file.", ComplianceProfile.HIPAA, null, "ssn_national_id 4-15 '000-12-3456'", "SSN [SSN_NATIONAL_ID] on file.")]
    [InlineData("Visit on 2026-01-15 and 01/20/2026 and 15-Jan-2026 and Jan 15, 2026.", ComplianceProfile.HIPAA, null, "dates 9-19 '2026-01-15'; dates 24-34 '01/20/2026'; dates 39-50 '15-Jan-2026'; dates 55-67 'Jan 15, 2026'", "Visit on [DATES] and [DATES] and [DATES] and [DATES].")]
    [InlineData("Recheck on 12/31/25 please.", ComplianceProfile.HIPAA, null, "dates 11-19 '12/31/25'", "Recheck on [DATES] please.")]
    // zip_geographic: US ZIP, Canadian and UK postcodes; a ZIP+4 ties with the telephone pattern on the same span
    // and telephone_fax wins the alphabetical tie-break
    [InlineData("ZIP 90210 on file.", ComplianceProfile.HIPAA, null, "zip_geographic 4-9 '90210'", "ZIP [ZIP_GEOGRAPHIC] on file.")]
    [InlineData("Postal code K1A 0B1 and SW1A 1AA noted.", ComplianceProfile.HIPAA, null, "zip_geographic 12-19 'K1A 0B1'; zip_geographic 24-32 'SW1A 1AA'", "Postal code [ZIP_GEOGRAPHIC] and [ZIP_GEOGRAPHIC] noted.")]
    [InlineData("Mail to ZIP 90210-1234 please.", ComplianceProfile.HIPAA, null, "telephone_fax 12-22 '90210-1234'", "Mail to ZIP [TELEPHONE_FAX] please.")]
    // urls
    [InlineData("See https://example.test/portal?id=7 for info.", ComplianceProfile.HIPAA, null, "urls 4-36 'https://example.test/portal?id=7'", "See [URLS] for info.")]
    [InlineData("Also www.example.test/path works.", ComplianceProfile.HIPAA, null, "urls 5-26 'www.example.test/path'", "Also [URLS] works.")]
    // ip_mac_addresses: IPv4, MAC, compressed and full IPv6
    [InlineData("Host 192.0.2.10 and MAC 00:1A:2B:3C:4D:5E logged.", ComplianceProfile.HIPAA, null, "ip_mac_addresses 5-15 '192.0.2.10'; ip_mac_addresses 24-41 '00:1A:2B:3C:4D:5E'", "Host [IP_MAC_ADDRESSES] and MAC [IP_MAC_ADDRESSES] logged.")]
    [InlineData("IPv6 2001:db8::1 logged.", ComplianceProfile.HIPAA, null, "ip_mac_addresses 5-16 '2001:db8::1'", "IPv6 [IP_MAC_ADDRESSES] logged.")]
    [InlineData("Loopback fe80::1 and full 2001:0db8:0000:0000:0000:ff00:0042:8329 seen.", ComplianceProfile.HIPAA, null, "ip_mac_addresses 9-16 'fe80::1'; ip_mac_addresses 26-65 '2001:0db8:0000:0000:0000:ff00:0042:8329'", "Loopback [IP_MAC_ADDRESSES] and full [IP_MAC_ADDRESSES] seen.")]
    // medical_record_account: prefixed identifiers; a bare ten-digit number counts only after an NHS/ACC/record/MRN/EHR
    // cue in the preceding 25 characters, otherwise it stays a telephone number; a prefix without a digit is ignored
    [InlineData("Chart MRN: 12345678 reviewed.", ComplianceProfile.HIPAA, null, "medical_record_account 6-19 'MRN: 12345678'", "Chart [MEDICAL_RECORD_ACCOUNT] reviewed.")]
    [InlineData("NHS number 900 000 0001 recorded.", ComplianceProfile.HIPAA, null, "medical_record_account 11-23 '900 000 0001'", "NHS number [MEDICAL_RECORD_ACCOUNT] recorded.")]
    [InlineData("Call 900 000 0001 now.", ComplianceProfile.HIPAA, null, "telephone_fax 5-17 '900 000 0001'", "Call [TELEPHONE_FAX] now.")]
    [InlineData("ACC 555-010-0199 billed.", ComplianceProfile.HIPAA, null, "medical_record_account 0-16 'ACC 555-010-0199'", "[MEDICAL_RECORD_ACCOUNT] billed.")]
    [InlineData("MRN pending review.", ComplianceProfile.HIPAA, null, "", "MRN pending review.")]
    // age
    [InlineData("Patient age: 97 and a 93-year-old and 91yo.", ComplianceProfile.HIPAA, null, "age 8-15 'age: 97'; age 22-33 '93-year-old'; age 38-42 '91yo'", "Patient [AGE] and a [AGE] and [AGE].")]
    [InlineData("Aged 45 and 45 years old.", ComplianceProfile.HIPAA, null, "age 0-7 'Aged 45'; age 12-24 '45 years old'", "[AGE] and [AGE].")]
    // health_plan_beneficiary, including trailing identifier punctuation; "Medicare ID:" is not a recognised prefix
    [InlineData("Medicare 0TEST00MBI1 was verified.", ComplianceProfile.HIPAA, null, "health_plan_beneficiary 0-20 'Medicare 0TEST00MBI1'", "[HEALTH_PLAN_BENEFICIARY] was verified.")]
    [InlineData("Health Plan: HP-778899 active.", ComplianceProfile.HIPAA, null, "health_plan_beneficiary 0-22 'Health Plan: HP-778899'", "[HEALTH_PLAN_BENEFICIARY] active.")]
    [InlineData("Policy Number: AB-12345-/ closed.", ComplianceProfile.HIPAA, null, "health_plan_beneficiary 0-25 'Policy Number: AB-12345-/'", "[HEALTH_PLAN_BENEFICIARY] closed.")]
    [InlineData("Medicare ID: 0TEST00MBI1 was verified.", ComplianceProfile.HIPAA, null, "", "Medicare ID: 0TEST00MBI1 was verified.")]
    // certificate_license
    [InlineData("Medical License: MD123456 issued.", ComplianceProfile.HIPAA, null, "certificate_license 0-25 'Medical License: MD123456'", "[CERTIFICATE_LICENSE] issued.")]
    [InlineData("Cert# 55-77889 checked.", ComplianceProfile.HIPAA, null, "certificate_license 0-14 'Cert# 55-77889'", "[CERTIFICATE_LICENSE] checked.")]
    // vehicle_identifiers
    [InlineData("VIN: 0FAKE00TEST000001 recorded.", ComplianceProfile.HIPAA, null, "vehicle_identifiers 0-22 'VIN: 0FAKE00TEST000001'", "[VEHICLE_IDENTIFIERS] recorded.")]
    [InlineData("Plate Number 7ABC123 seen.", ComplianceProfile.HIPAA, null, "vehicle_identifiers 0-20 'Plate Number 7ABC123'", "[VEHICLE_IDENTIFIERS] seen.")]
    // device_serial
    [InlineData("Device ID: SN-000123-A implanted.", ComplianceProfile.HIPAA, null, "device_serial 0-22 'Device ID: SN-000123-A'", "[DEVICE_SERIAL] implanted.")]
    [InlineData("Serial Number 12345-XYZ logged.", ComplianceProfile.HIPAA, null, "device_serial 0-23 'Serial Number 12345-XYZ'", "[DEVICE_SERIAL] logged.")]
    [InlineData("Model Number: X100-2000 used.", ComplianceProfile.HIPAA, null, "device_serial 0-23 'Model Number: X100-2000'", "[DEVICE_SERIAL] used.")]
    // custom terms: case-insensitive; short word-like terms need word boundaries, so "QTc" is not a match
    [InlineData("Seen by Dr. Q. Tester (QT) at the clinic.", ComplianceProfile.HIPAA, "Q. Tester|QT", "custom 12-21 'Q. Tester'; custom 23-25 'QT'", "Seen by Dr. [CUSTOM] ([CUSTOM]) at the clinic.")]
    [InlineData("QTc interval normal; qt noted by QT.", ComplianceProfile.HIPAA, "QT", "custom 21-23 'qt'; custom 33-35 'QT'", "QTc interval normal; [CUSTOM] noted by [CUSTOM].")]
    // profile filtering: GDPR matches HIPAA; EU_CTR keeps only direct patient identifiers
    [InlineData("Visit 2026-01-15 at 192.0.2.10 for MRN-4455 via https://example.test/a", ComplianceProfile.GDPR, null, "dates 6-16 '2026-01-15'; ip_mac_addresses 20-30 '192.0.2.10'; medical_record_account 35-43 'MRN-4455'; urls 48-70 'https://example.test/a'", "Visit [DATES] at [IP_MAC_ADDRESSES] for [MEDICAL_RECORD_ACCOUNT] via [URLS]")]
    [InlineData("Visit 2026-01-15 at 192.0.2.10 for MRN-4455 via https://example.test/a", ComplianceProfile.EU_CTR, null, "dates 6-16 '2026-01-15'; medical_record_account 35-43 'MRN-4455'", "Visit [DATES] at 192.0.2.10 for [MEDICAL_RECORD_ACCOUNT] via https://example.test/a")]
    [InlineData("Postal code K1A 0B1 and ZIP 90210 noted.", ComplianceProfile.EU_CTR, null, "", "Postal code K1A 0B1 and ZIP 90210 noted.")]
    [InlineData("Nobody here: test.user@example.org, ZIP 90210, Device ID: SN-000123-A.", ComplianceProfile.EU_CTR, null, "email 13-34 'test.user@example.org'", "Nobody here: [EMAIL], ZIP 90210, Device ID: SN-000123-A.")]
    public void Detect_MatchesPythonReference(
        string text,
        ComplianceProfile profile,
        string? customTerms,
        string expectedDetections,
        string expectedRedaction)
    {
        var terms = customTerms?.Split('|').ToList();

        var results = new DeidDetector().Detect(text, profile, terms);

        Describe(results).Should().Be(expectedDetections);
        DeidDetector.RedactText(text, results).Should().Be(expectedRedaction);
    }

    /// <summary>
    /// Empty text, and custom terms that are blank, detect nothing.
    /// </summary>
    [Fact]
    public void Detect_ReturnsNothing_ForEmptyTextOrBlankCustomTerms()
    {
        var detector = new DeidDetector();

        detector.Detect(string.Empty).Should().BeEmpty();
        detector.Detect("Reviewed by the site team.", ComplianceProfile.HIPAA, ["", "   "]).Should().BeEmpty();
    }

    /// <summary>
    /// HIPAA and GDPR enable all 14 categories; EU_CTR enables the seven direct patient identifier categories.
    /// </summary>
    [Fact]
    public void GetCategoriesForProfile_FiltersCategoriesByProfile()
    {
        string[] all =
        [
            DetectorCategory.Email, DetectorCategory.TelephoneFax, DetectorCategory.SsnNationalId, DetectorCategory.Dates,
            DetectorCategory.ZipGeographic, DetectorCategory.Urls, DetectorCategory.IpMacAddresses,
            DetectorCategory.MedicalRecordAccount, DetectorCategory.Age, DetectorCategory.Custom,
            DetectorCategory.HealthPlanBeneficiary, DetectorCategory.CertificateLicense,
            DetectorCategory.VehicleIdentifiers, DetectorCategory.DeviceSerial,
        ];

        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.HIPAA).Should().BeEquivalentTo(all);
        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.GDPR).Should().BeEquivalentTo(all);
        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.EU_CTR).Should().BeEquivalentTo(
            DetectorCategory.Email, DetectorCategory.TelephoneFax, DetectorCategory.SsnNationalId,
            DetectorCategory.Dates, DetectorCategory.MedicalRecordAccount, DetectorCategory.Age,
            DetectorCategory.Custom);
        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.EU_CTR).Should().NotContain(EuCtrExcludedCategories);
    }

    /// <summary>
    /// The returned set is a copy, so a caller changing it cannot change a profile.
    /// </summary>
    [Fact]
    public void GetCategoriesForProfile_ReturnsIndependentCopy()
    {
        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.EU_CTR).Add(DetectorCategory.Urls);

        ProfileCategories.GetCategoriesForProfile(ComplianceProfile.EU_CTR).Should().NotContain(DetectorCategory.Urls);
    }

    /// <summary>
    /// Overlaps resolve as in the Python: earliest start, then widest span, then category name, then longest value;
    /// adjacent spans do not overlap; the result is ordered by start.
    /// </summary>
    [Fact]
    public void ResolveOverlaps_KeepsEarliestWidestThenAlphabeticalCandidate()
    {
        var candidates = new List<DetectionResult>
        {
            new(DetectorCategory.Custom, 12, 15, "abc"),
            new(DetectorCategory.Dates, 0, 10, "0123456789"),
            new(DetectorCategory.Email, 2, 5, "234"),
            new(DetectorCategory.Urls, 0, 12, "0123456789ab"),
            new(DetectorCategory.Age, 0, 10, "0123456789"),
        };

        Describe(DeidDetector.ResolveOverlaps(candidates))
            .Should().Be("urls 0-12 '0123456789ab'; custom 12-15 'abc'");

        var tie = new List<DetectionResult>
        {
            new(DetectorCategory.Dates, 0, 10, "0123456789"),
            new(DetectorCategory.Age, 0, 10, "0123456789"),
            new(DetectorCategory.Email, 20, 25, "01234"),
        };

        Describe(DeidDetector.ResolveOverlaps(tie))
            .Should().Be("age 0-10 '0123456789'; email 20-25 '01234'");
    }

    /// <summary>
    /// Redaction resolves overlaps itself, replaces right to left so offsets stay valid, and uses a caller-supplied
    /// placeholder when given one.
    /// </summary>
    [Fact]
    public void RedactText_ResolvesOverlapsAndUsesPlaceholderFunction()
    {
        const string text = "Call 555-010-0199 or mail test.user@example.org.";
        var candidates = new List<DetectionResult>
        {
            new(DetectorCategory.Email, 26, 47, "test.user@example.org"),
            new(DetectorCategory.TelephoneFax, 5, 17, "555-010-0199"),
            new(DetectorCategory.MedicalRecordAccount, 5, 12, "555-010"),
        };

        DeidDetector.RedactText(text, candidates)
            .Should().Be("Call [TELEPHONE_FAX] or mail [EMAIL].");
        DeidDetector.RedactText(text, candidates, r => $"<{r.Category}:{r.End - r.Start}>")
            .Should().Be("Call <telephone_fax:12> or mail <email:21>.");
        DeidDetector.RedactText(text, []).Should().Be(text);
    }

    private static string Describe(IEnumerable<DetectionResult> results) =>
        string.Join("; ", results.Select(r => $"{r.Category} {r.Start}-{r.End} '{r.Value}'"));
}
