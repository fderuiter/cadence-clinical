using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Devices;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Repository port for investigational medical device inventory, deficiency reports, and RTSM quarantine records.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public interface IDeviceRepository
{
    Task<DeviceInventoryItem?> GetInventoryByIdAsync(Guid id, CancellationToken ct = default);

    Task<DeviceInventoryItem?> GetInventoryBySerialOrDiAsync(string studyId, string udiDi, string? serialNumber, string? lotNumber, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceInventoryItem>> ListInventoryAsync(string studyId, string? siteId, string? status, string? udiDi, string? lotNumber, string? serialNumber, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceInventoryItem>> FindMatchingDevicesForQuarantineAsync(string studyId, string siteId, string scope, string udiDi, string lotNumber, string? serialNumber, string? deviceInventoryId, CancellationToken ct = default);

    Task AddInventoryAsync(DeviceInventoryItem device, CancellationToken ct = default);

    Task AddInventoryRangeAsync(IEnumerable<DeviceInventoryItem> devices, CancellationToken ct = default);

    Task<DeviceDeficiency?> GetDeficiencyByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceDeficiency>> ListDeficienciesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default);

    Task AddDeficiencyAsync(DeviceDeficiency deficiency, CancellationToken ct = default);

    Task<DeviceQuarantineRecord?> GetQuarantineByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceQuarantineRecord>> ListQuarantinesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default);

    Task AddQuarantineRecordAsync(DeviceQuarantineRecord record, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
