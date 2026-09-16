using System.Collections.Generic;

namespace Cadence.Deid;

public static class DetectorCategory
{
    public const string Email = "email";
    public const string TelephoneFax = "telephone_fax";
    public const string SsnNationalId = "ssn_national_id";
    public const string Dates = "dates";
    public const string ZipGeographic = "zip_geographic";
    public const string Urls = "urls";
    public const string IpMacAddresses = "ip_mac_addresses";
    public const string MedicalRecordAccount = "medical_record_account";
    public const string Age = "age";
    public const string Custom = "custom";
    public const string HealthPlanBeneficiary = "health_plan_beneficiary";
    public const string CertificateLicense = "certificate_license";
    public const string VehicleIdentifiers = "vehicle_identifiers";
    public const string DeviceSerial = "device_serial";
}

public enum ComplianceProfile
{
    HIPAA,
    GDPR,
    EU_CTR
}

public static class ProfileCategories
{
    public static readonly HashSet<string> AllCategories = new()
    {
        DetectorCategory.Email,
        DetectorCategory.TelephoneFax,
        DetectorCategory.SsnNationalId,
        DetectorCategory.Dates,
        DetectorCategory.ZipGeographic,
        DetectorCategory.Urls,
        DetectorCategory.IpMacAddresses,
        DetectorCategory.MedicalRecordAccount,
        DetectorCategory.Age,
        DetectorCategory.Custom,
        DetectorCategory.HealthPlanBeneficiary,
        DetectorCategory.CertificateLicense,
        DetectorCategory.VehicleIdentifiers,
        DetectorCategory.DeviceSerial
    };

    public static readonly HashSet<string> EuCtrCategories = new()
    {
        DetectorCategory.Email,
        DetectorCategory.TelephoneFax,
        DetectorCategory.SsnNationalId,
        DetectorCategory.Dates,
        DetectorCategory.MedicalRecordAccount,
        DetectorCategory.Age,
        DetectorCategory.Custom
    };

    public static HashSet<string> GetCategoriesForProfile(ComplianceProfile profile)
    {
        return profile switch
        {
            ComplianceProfile.EU_CTR => EuCtrCategories,
            _ => AllCategories
        };
    }
}

public record DetectionResult(
    string Category,
    int Start,
    int End,
    string Value
);
