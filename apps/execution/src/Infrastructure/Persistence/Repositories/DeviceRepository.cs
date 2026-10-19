using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core persistence adapter for <see cref="IDeviceRepository"/>.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public sealed class DeviceRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IDeviceRepository
{
    public async Task<DeviceInventoryItem?> GetInventoryByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await Context.DeviceInventoryItems
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<DeviceInventoryItem?> GetInventoryBySerialOrDiAsync(string studyId, string udiDi, string? serialNumber, string? lotNumber, CancellationToken ct = default)
    {
        var query = Context.DeviceInventoryItems
            .Where(x => x.StudyId == studyId && x.UdiDi == udiDi);

        if (!string.IsNullOrEmpty(serialNumber))
        {
            return await query.FirstOrDefaultAsync(x => x.SerialNumber == serialNumber, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(lotNumber))
        {
            return await query.FirstOrDefaultAsync(x => x.LotNumber == lotNumber, ct).ConfigureAwait(false);
        }

        return await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceInventoryItem>> ListInventoryAsync(string studyId, string? siteId, string? status, string? udiDi, string? lotNumber, string? serialNumber, CancellationToken ct = default)
    {
        var query = Context.DeviceInventoryItems
            .AsNoTracking()
            .Where(x => x.StudyId == studyId);

        if (!string.IsNullOrEmpty(siteId))
        {
            query = query.Where(x => x.SiteId == siteId);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrEmpty(udiDi))
        {
            query = query.Where(x => x.UdiDi == udiDi);
        }

        if (!string.IsNullOrEmpty(lotNumber))
        {
            query = query.Where(x => x.LotNumber == lotNumber);
        }

        if (!string.IsNullOrEmpty(serialNumber))
        {
            query = query.Where(x => x.SerialNumber == serialNumber);
        }

        var items = await query.ToListAsync(ct).ConfigureAwait(false);
        return items;
    }

    public async Task<IReadOnlyList<DeviceInventoryItem>> FindMatchingDevicesForQuarantineAsync(string studyId, string siteId, string scope, string udiDi, string lotNumber, string? serialNumber, string? deviceInventoryId, CancellationToken ct = default)
    {
        var query = Context.DeviceInventoryItems
            .Where(x => x.StudyId == studyId);

        if (!string.IsNullOrEmpty(deviceInventoryId) && Guid.TryParse(deviceInventoryId, out var devGuid))
        {
            query = query.Where(x => x.Id == devGuid);
        }
        else if (string.Equals(scope, "DEVICE_SERIAL", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(serialNumber))
        {
            query = query.Where(x => x.UdiDi == udiDi && x.SerialNumber == serialNumber);
        }
        else if (string.Equals(scope, "DEVICE_LOT", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.UdiDi == udiDi && x.LotNumber == lotNumber);
        }
        else if (string.Equals(scope, "SITE_INVENTORY", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SiteId == siteId);
        }
        // STUDY_WIDE matches all devices in the study

        var items = await query.ToListAsync(ct).ConfigureAwait(false);
        return items;
    }

    public async Task AddInventoryAsync(DeviceInventoryItem device, CancellationToken ct = default)
    {
        await Context.DeviceInventoryItems.AddAsync(device, ct).ConfigureAwait(false);
    }

    public async Task AddInventoryRangeAsync(IEnumerable<DeviceInventoryItem> devices, CancellationToken ct = default)
    {
        await Context.DeviceInventoryItems.AddRangeAsync(devices, ct).ConfigureAwait(false);
    }

    public async Task<DeviceDeficiency?> GetDeficiencyByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await Context.DeviceDeficiencies
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceDeficiency>> ListDeficienciesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default)
    {
        var query = Context.DeviceDeficiencies
            .AsNoTracking()
            .Where(x => x.StudyId == studyId);

        if (!string.IsNullOrEmpty(siteId))
        {
            query = query.Where(x => x.SiteId == siteId);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(x => x.InvestigationStatus == status);
        }

        var items = await query.ToListAsync(ct).ConfigureAwait(false);
        return items;
    }

    public async Task AddDeficiencyAsync(DeviceDeficiency deficiency, CancellationToken ct = default)
    {
        await Context.DeviceDeficiencies.AddAsync(deficiency, ct).ConfigureAwait(false);
    }

    public async Task<DeviceQuarantineRecord?> GetQuarantineByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await Context.DeviceQuarantineRecords
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceQuarantineRecord>> ListQuarantinesAsync(string studyId, string? siteId, string? status, CancellationToken ct = default)
    {
        var query = Context.DeviceQuarantineRecords
            .AsNoTracking()
            .Where(x => x.StudyId == studyId);

        if (!string.IsNullOrEmpty(siteId))
        {
            query = query.Where(x => x.SiteId == siteId);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var items = await query.ToListAsync(ct).ConfigureAwait(false);
        return items;
    }

    public async Task AddQuarantineRecordAsync(DeviceQuarantineRecord record, CancellationToken ct = default)
    {
        await Context.DeviceQuarantineRecords.AddAsync(record, ct).ConfigureAwait(false);
    }
}
