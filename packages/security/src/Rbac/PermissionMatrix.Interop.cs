namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The Interop module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddInteropGrants()
    {
        const string Endpoint = "esource_endpoint";
        const string Candidate = "esource_candidate";

        // Hospital EHR connections and their SMART credentials are configured by sponsor data management and system
        // administrators; site staff and monitors can see which systems are connected.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, Endpoint, "read", "configure");
            Grant(role, Candidate, "ingest");
        }

        foreach (string role in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRA_CANONICAL })
        {
            Grant(role, Endpoint, "read");
        }

        // Site staff pull EHR data for their participants and decide whether it enters the eCRF.
        foreach (string role in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR })
        {
            Grant(role, Candidate, "ingest", "reconcile");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRA_CANONICAL, "monitor",
            RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, Candidate, "read");
        }

        // Monitors and data management raise discrepancy queries on candidates; they never accept or reject them.
        foreach (string role in new[] { RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, Candidate, "query");
        }
    }
}
