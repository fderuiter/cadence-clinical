namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The Imaging module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddImagingGrants()
    {
        const string Study = "imaging_study";
        const string Adjudication = "imaging_adjudication";

        // Sites upload scans for their participants; data management loads central-reader transfers.
        foreach (string role in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, Study, "upload");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRA_CANONICAL, "monitor",
            RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, Study, "read");
        }

        // Independent review is a sponsor function: data management records the two readers' assessments, and the
        // medical monitor chairs a discordant case. Site staff never see or decide adjudications.
        Grant(RoleNormalization.ROLE_SPONSOR_DM, Adjudication, "submit", "read");
        Grant(RoleNormalization.ROLE_SPONSOR_MM, Adjudication, "submit", "read", "adjudicate");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, Adjudication, "read");
    }
}
