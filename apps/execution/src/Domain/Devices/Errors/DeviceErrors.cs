using System;

namespace Cadence.Apps.Execution.Domain.Devices.Errors;

/// <summary>
/// Domain error messages and codes for medical device inventory and RTSM quarantine operations.
/// </summary>
public static class DeviceErrors
{
    public const string DeviceNotFoundCode = "DEVICE_NOT_FOUND";
    public const string DeviceQuarantinedCode = "DEVICE_QUARANTINED";
    public const string DeviceExpiredCode = "DEVICE_EXPIRED";
    public const string DeviceAlreadyDispensedCode = "DEVICE_ALREADY_DISPENSED";
    public const string InvalidUdiBarcodeCode = "INVALID_UDI_BARCODE";

    public static Exception NotFound(string id) =>
        new KeyNotFoundException($"Investigational device '{id}' was not found.");

    public static Exception Quarantined(string id, string reason) =>
        new InvalidOperationException($"Dispensation blocked: Investigational device '{id}' is marked QUARANTINED in RTSM. Reason: {reason}");

    public static Exception Expired(string id, DateTimeOffset expDate) =>
        new InvalidOperationException($"Dispensation blocked: Investigational device '{id}' expired on {expDate:yyyy-MM-dd}.");

    public static Exception AlreadyDispensed(string id) =>
        new InvalidOperationException($"Investigational device '{id}' has already been dispensed.");
}
