namespace Cadence.Apps.Notifications.Infrastructure;

/// <summary>
/// VAPID options for Web Push authentication.
/// </summary>
public class VapidOptions
{
    public const string Position = "Vapid";

    public string Subject { get; set; } = "mailto:support@cadence.clinical";

    public string PublicKey { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;
}
