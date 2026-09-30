using System;

using Cadence.Security.FieldProtection;

namespace Cadence.Apps.Interop.Infrastructure.Security;

/// <summary>
/// Turns a raw wearable/sensor device token into the keyed lookup hash stored in every <c>DeviceTokenHash</c>
/// column (device registrations, telemetry streams, daily summaries, safety alerts). The raw token is never
/// persisted; to find a device's rows, hash the presented token and compare hashes (#4770).
/// @req:PRD-SYS-053
/// </summary>
public sealed class DeviceTokenHasher(ILookupHasher lookupHasher)
{
    /// <summary>Hash purpose shared by all interop device-token columns, so the same token matches across tables.</summary>
    public const string Purpose = "interop.device_token";

    /// <summary>Returns the 64-character lowercase hex HMAC-SHA256 of <paramref name="rawDeviceToken"/>.</summary>
    public string Hash(string rawDeviceToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDeviceToken);
        return lookupHasher.ComputeHash(rawDeviceToken, Purpose);
    }
}
