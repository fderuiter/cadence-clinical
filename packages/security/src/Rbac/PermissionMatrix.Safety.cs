namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>
    /// The Safety module's route permissions (ADR-2516, #5462). Approving a periodic safety report (DSUR) is a 21 CFR
    /// Part 11 electronic signature by a qualified safety physician or medical monitor (PRD-PVG-008), so it is its own
    /// permission, separate from <c>safety_report:create</c>. Only the sponsor medical monitor holds it; the system
    /// administrator compiles and reads reports but does not sign them. Compiling and approving share the medical
    /// monitor role, so the handler refuses an approval from the report's compiler (separation of duties).
    /// </summary>
    private static partial void AddSafetyGrants()
    {
        Grant(RoleNormalization.ROLE_SPONSOR_MM, "safety_report", "approve");
    }
}
