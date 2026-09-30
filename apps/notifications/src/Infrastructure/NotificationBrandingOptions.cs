using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Notifications.Infrastructure;

/// <summary>
/// The brand name and portal link notifications show, bound from the <c>Notifications</c> configuration section
/// (#4983). A caller cannot set them, so a rendered notification can only link to the configured portal.
/// </summary>
public sealed class NotificationBrandingOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Notifications";

    /// <summary>The message startup fails with when <see cref="PortalUrl"/> is not allowed.</summary>
    public const string InvalidPortalUrlMessage =
        "Notifications:PortalUrl must be an absolute https URL (http is allowed only for localhost).";

    /// <summary>The message startup fails with when <see cref="BrandName"/> is empty.</summary>
    public const string MissingBrandNameMessage = "Notifications:BrandName must not be empty.";

    /// <summary>The platform name shown in notification footers.</summary>
    public string BrandName { get; set; } = "Cadence Clinical Platform";

    /// <summary>The participant portal's sign-in URL. Without one, notifications carry no portal link.</summary>
    public string? PortalUrl { get; set; }

    /// <summary>
    /// Whether <paramref name="value"/> may be used as the portal link: unset, or an absolute <c>https</c> URL, or
    /// <c>http</c> on a loopback host for local development.
    /// </summary>
    public static bool IsValidPortalUrl(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)));
}

/// <summary>Registers <see cref="NotificationBrandingOptions"/>, validated when the host starts.</summary>
public static class NotificationBrandingServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="NotificationBrandingOptions"/> from the <c>Notifications</c> section and refuses to start with
    /// an empty brand name or a portal URL that is not absolute <c>https</c> (#4983).
    /// </summary>
    public static IServiceCollection AddNotificationBranding(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<NotificationBrandingOptions>()
            .Bind(configuration.GetSection(NotificationBrandingOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BrandName), NotificationBrandingOptions.MissingBrandNameMessage)
            .Validate(options => NotificationBrandingOptions.IsValidPortalUrl(options.PortalUrl), NotificationBrandingOptions.InvalidPortalUrlMessage)
            .ValidateOnStart();
        return services;
    }
}
