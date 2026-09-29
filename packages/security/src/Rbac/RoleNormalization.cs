using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Provides role normalization mapping role aliases to canonical platform role names.
/// @req:PRD-SYS-053
/// </summary>
public static class RoleNormalization
{
    // Legacy constants
    public const string ROLE_CRA = "CRA";
    public const string ROLE_DATA_MANAGER = "Data Manager";
    public const string ROLE_SITE_INVESTIGATOR = "Site Investigator";
    public const string ROLE_AUDITOR = "Auditor";
    public const string ROLE_SPONSOR_ADMIN = "Sponsor Admin";

    // Canonical lower-case roles
    public const string ROLE_SYSADMIN = "sysadmin";
    public const string ROLE_SPONSOR_DESIGNER = "sponsor_designer";
    public const string ROLE_SPONSOR_DM = "sponsor_dm";
    public const string ROLE_SPONSOR_MM = "sponsor_mm";
    public const string ROLE_SPONSOR_STATISTICIAN = "sponsor_statistician";
    public const string ROLE_INVESTIGATOR = "investigator";
    public const string ROLE_CRC = "crc";
    public const string ROLE_CRA_CANONICAL = "cra";
    public const string ROLE_SUBJECT = "subject";
    public const string ROLE_AUDITOR_CANONICAL = "auditor";
    public const string ROLE_EXTERNAL_MONITOR = "external_monitor";
    public const string ROLE_REVIEWER = "protocol_reviewer";

    // RTSM roles
    public const string ROLE_PHARMACIST = "pharmacist";
    public const string ROLE_UNBLINDED_STATISTICIAN = "unblinded_statistician";
    public const string ROLE_IDMC = "idmc";
    public const string ROLE_EMERGENCY_UNBLINDER = "emergency_unblinder";
    public const string ROLE_PRINCIPAL_INVESTIGATOR = "principal_investigator";
    public const string ROLE_AUTHORIZED_ER_PHYSICIAN = "authorized_er_physician";
    public const string ROLE_LEAD_INVESTIGATOR = "lead_investigator";

    public static IReadOnlySet<string> AUDITOR_ROLES { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "auditor",
        "inspector",
        "regulatory_inspector"
    };

    public static IReadOnlyDictionary<string, string> ROLE_ALIASES { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["unblinded statistician"] = ROLE_UNBLINDED_STATISTICIAN,
        ["lead unblinded statistician"] = ROLE_UNBLINDED_STATISTICIAN,
        ["unblinded_statistician"] = ROLE_UNBLINDED_STATISTICIAN,
        ["idmc"] = ROLE_IDMC,
        ["dsmb"] = ROLE_IDMC,
        ["pharmacist"] = ROLE_PHARMACIST,
        ["unblinded pharmacist"] = ROLE_PHARMACIST,
        ["unblinded_pharmacist"] = ROLE_PHARMACIST,
        ["emergency unblinder"] = ROLE_EMERGENCY_UNBLINDER,
        ["emergency_unblinder"] = ROLE_EMERGENCY_UNBLINDER,
        ["protocol_reviewer"] = ROLE_REVIEWER,
        ["protocol reviewer"] = ROLE_REVIEWER,
        ["protocol-reviewer"] = ROLE_REVIEWER,
        ["reviewer"] = ROLE_REVIEWER,
        ["external monitor"] = ROLE_EXTERNAL_MONITOR,
        ["external_monitor"] = ROLE_EXTERNAL_MONITOR,
        ["external-monitor"] = ROLE_EXTERNAL_MONITOR,
        ["cro monitor"] = ROLE_EXTERNAL_MONITOR,
        ["cro_monitor"] = ROLE_EXTERNAL_MONITOR,
        ["cro-monitor"] = ROLE_EXTERNAL_MONITOR,
        ["sysadmin"] = ROLE_SYSADMIN,
        ["system administrator"] = ROLE_SYSADMIN,
        ["system_admin"] = ROLE_SYSADMIN,
        ["system-admin"] = ROLE_SYSADMIN,
        ["sponsor study designer"] = ROLE_SPONSOR_DESIGNER,
        ["sponsor_designer"] = ROLE_SPONSOR_DESIGNER,
        ["sponsor-designer"] = ROLE_SPONSOR_DESIGNER,
        ["designer"] = ROLE_SPONSOR_DESIGNER,
        ["study_designer"] = ROLE_SPONSOR_DESIGNER,
        ["study-designer"] = ROLE_SPONSOR_DESIGNER,
        ["study designer"] = ROLE_SPONSOR_DESIGNER,
        ["sponsor designer"] = ROLE_SPONSOR_DESIGNER,
        ["sponsor data manager"] = ROLE_SPONSOR_DM,
        ["sponsor_dm"] = ROLE_SPONSOR_DM,
        ["sponsor-dm"] = ROLE_SPONSOR_DM,
        ["sponsor dm"] = ROLE_SPONSOR_DM,
        ["data manager"] = ROLE_SPONSOR_DM,
        ["data_manager"] = ROLE_SPONSOR_DM,
        ["data-manager"] = ROLE_SPONSOR_DM,
        ["dm"] = ROLE_SPONSOR_DM,
        ["admin"] = ROLE_SPONSOR_DM,
        ["sponsor admin"] = ROLE_SPONSOR_DM,
        ["sponsor_admin"] = ROLE_SPONSOR_DM,
        ["sponsor medical monitor"] = ROLE_SPONSOR_MM,
        ["sponsor_mm"] = ROLE_SPONSOR_MM,
        ["sponsor-mm"] = ROLE_SPONSOR_MM,
        ["sponsor mm"] = ROLE_SPONSOR_MM,
        ["medical monitor"] = ROLE_SPONSOR_MM,
        ["medical_monitor"] = ROLE_SPONSOR_MM,
        ["mm"] = ROLE_SPONSOR_MM,
        ["sponsor statistician"] = ROLE_SPONSOR_STATISTICIAN,
        ["sponsor_statistician"] = ROLE_SPONSOR_STATISTICIAN,
        ["sponsor-statistician"] = ROLE_SPONSOR_STATISTICIAN,
        ["statistician"] = ROLE_SPONSOR_STATISTICIAN,
        ["investigator"] = ROLE_INVESTIGATOR,
        ["site investigator"] = ROLE_INVESTIGATOR,
        ["site_investigator"] = ROLE_INVESTIGATOR,
        ["site-investigator"] = ROLE_INVESTIGATOR,
        ["principal investigator"] = ROLE_PRINCIPAL_INVESTIGATOR,
        ["pi"] = ROLE_PRINCIPAL_INVESTIGATOR,
        ["principal_investigator"] = ROLE_PRINCIPAL_INVESTIGATOR,
        ["principalinvestigator"] = ROLE_PRINCIPAL_INVESTIGATOR,
        ["authorized er physician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["authorized_er_physician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["authorized-er-physician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["authorederphysician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["er physician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["er_physician"] = ROLE_AUTHORIZED_ER_PHYSICIAN,
        ["lead investigator"] = ROLE_LEAD_INVESTIGATOR,
        ["lead_investigator"] = ROLE_LEAD_INVESTIGATOR,
        ["lead-investigator"] = ROLE_LEAD_INVESTIGATOR,
        ["leadinvestigator"] = ROLE_LEAD_INVESTIGATOR,
        ["investigator_user"] = ROLE_INVESTIGATOR,
        ["crc"] = ROLE_CRC,
        ["site crc"] = ROLE_CRC,
        ["site_crc"] = ROLE_CRC,
        ["site-crc"] = ROLE_CRC,
        ["clinical research coordinator"] = ROLE_CRC,
        ["study coordinator"] = ROLE_CRC,
        ["study_coordinator"] = ROLE_CRC,
        ["study-coordinator"] = ROLE_CRC,
        ["coordinator"] = ROLE_CRC,
        ["cra"] = ROLE_CRA_CANONICAL,
        ["clinical research associate"] = ROLE_CRA_CANONICAL,
        ["monitor"] = "monitor",
        ["cra/monitor"] = ROLE_CRA_CANONICAL,
        ["cra_monitor"] = ROLE_CRA_CANONICAL,
        ["cra-monitor"] = ROLE_CRA_CANONICAL,
        ["subject"] = ROLE_SUBJECT,
        ["patient"] = ROLE_SUBJECT,
        ["epro"] = ROLE_SUBJECT,
        ["auditor"] = ROLE_AUDITOR_CANONICAL,
        ["inspector"] = ROLE_AUDITOR_CANONICAL,
        ["regulatory_inspector"] = ROLE_AUDITOR_CANONICAL,
    };

    /// <summary>
    /// Normalizes a role string to its canonical form using ROLE_ALIASES.
    /// </summary>
    public static string NormalizeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return string.Empty;
        string norm = role.Trim().ToLowerInvariant();
        return ROLE_ALIASES.TryGetValue(norm, out string? canonical) ? canonical : norm;
    }

    /// <summary>
    /// Returns a list of normalized canonical role names from an input collection of roles.
    /// </summary>
    public static IReadOnlyList<string> GetNormalizedRoles(IEnumerable<string>? roles)
    {
        if (roles == null) return Array.Empty<string>();
        return roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
