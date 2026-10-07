using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Devices;

/// <summary>
/// Application service contract for medical device barcode parsing, inventory intake, dispensation gating, deficiency reporting, and RTSM quarantine workflows.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public interface IDeviceApplicationService
{
    ParsedUdiResponse ParseUdi(UdiParseRequest request);

    Task<IReadOnlyList<DeviceInventoryResponse>> RegisterInventoryAsync(DeviceInventoryCreateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceInventoryResponse>> ListInventoryAsync(string studyId, string? siteId, string? status, string? udiDi, string? lotNumber, string? serialNumber, CancellationToken ct = default);

    Task<DeviceDispensationResponse> DispenseDeviceAsync(DeviceDispensationRequest request, CancellationToken ct = default);

    Task<DeviceDeficiencyResponse> LogDeficiencyAsync(DeviceDeficiencyCreateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceDeficiencyResponse>> ListDeficienciesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default);

    Task<DeviceDeficiencyResponse?> GetDeficiencyByIdAsync(Guid id, CancellationToken ct = default);

    Task<DeviceQuarantineResponse> InitiateQuarantineAsync(DeviceQuarantineCreateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceQuarantineResponse>> ListQuarantinesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default);

    Task<DeviceQuarantineResponse> ReleaseQuarantineAsync(DeviceQuarantineReleaseRequest request, CancellationToken ct = default);

    Task<DeviceInvestigationSummaryResponse> GetInvestigationSummaryAsync(string studyId, CancellationToken ct = default);
}
