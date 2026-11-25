namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The eTMF module's route permissions (ADR-2510, ADR-2516), added to the eTMF grants in the role table.</summary>
    private static partial void AddEtmfGrants()
    {
        // Site coordinators file and stage site documents for CRA QC; CRAs and monitors already tag, create and run
        // clinical QC, and data managers run the later QC transitions (PermissionMatrix.cs).
        Grant(RoleNormalization.ROLE_CRC, "etmf_document", "tag", "create");

        // Exporting a study binder copies every document in it, so it is its own permission: sponsor data management,
        // monitors and inspection staff, never site roles.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "etmf_document", "export");
        }
        // Signed service-to-service filing of finalized GxP artifacts (05.02.04 DoA Log, 05.02.06 Visit Letters) from CTMS.
        Grant(CtmsServiceRole, "etmf_document", "read", "create", "approve_filing");
        Grant(CtmsServiceRole, "etmf_edl", "read");
        Grant(CtmsServiceRole, "etmf_taxonomy", "read");
    }
}
