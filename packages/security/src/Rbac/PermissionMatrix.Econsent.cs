namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The eConsent module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddEconsentGrants()
    {
        // Sponsor study build publishes consent templates; publishing can require every participant to re-consent.
        foreach (string role in new[] { RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_SPONSOR_DESIGNER })
        {
            Grant(role, "econsent_template", "publish");
        }

        // Site staff conduct the consent discussion and capture the participant's signature.
        foreach (string role in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR })
        {
            Grant(role, "econsent_consent", "capture");
        }
    }
}
