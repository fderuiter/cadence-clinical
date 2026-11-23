using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Supply;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Supply;

public sealed class IpKitApplicationServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ExecutionDbContext _dbContext;
    private readonly IClock _clock;

    private static readonly AesGcmAllocationProtector Protector = new(Options.Create(
        new AllocationProtectionOptions { KeyBase64 = TestSecurityContext.RandomizationKeyBase64 }));

    public IpKitApplicationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ExecutionDbContext(options);
        _dbContext.Database.EnsureCreated();

        _clock = SystemClock.Instance;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    [GxPRequirement("PRD-SCM-006")]
    public async Task ProcessBatchReconciliationAsync_UpdatesKitStatuses_AndGeneratesCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new IpKitApplicationService(_dbContext, _clock, Protector);

        var seedKits = new List<IpKitSeedItem>
        {
            new("KIT-001", "LOT-A100", _clock.UtcNow.AddMonths(12), "ACTIVE_AT_SITE"),
            new("KIT-002", "LOT-A100", _clock.UtcNow.AddMonths(12), "ACTIVE_AT_SITE")
        };
        await service.SeedSiteInventoryAsync("STUDY-101", "SITE-01", seedKits, ct);

        var request = new IpKitBatchReconciliationRequest(
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            Items: new List<IpKitItemReconcileRequest>
            {
                new("KIT-001", "LOT-A100", "LOT-A100", 15.5, 100.0, "RETURNED_TO_SITE", "NORMAL_RETURN"),
                new("KIT-002", "LOT-A100", "LOT-A100", 0.0, 0.0, "QUARANTINED_FOR_DESTRUCTION", "DAMAGED_SEAL")
            },
            ProcessedBy: "pharmacist_jane",
            Notes: "Bulk return carton #12",
            ReasonForChange: "Pharmacist bulk reconciliation intake");

        var response = await service.ProcessBatchReconciliationAsync(request, ct);

        Assert.NotNull(response);
        Assert.Equal("STUDY-101", response.StudyId);
        Assert.Equal("SITE-01", response.SiteId);
        Assert.Equal(2, response.TotalKits);
        Assert.Equal(1, response.QuarantinedKits);
        Assert.NotEmpty(response.CertificateId);
        Assert.NotNull(response.Certificate);

        var inventory = await service.GetSiteInventoryAsync("STUDY-101", "SITE-01", ct);
        Assert.Equal(2, inventory.Count);

        var kit1 = inventory.Find(k => k.KitNumber == "KIT-001");
        Assert.NotNull(kit1);
        Assert.Equal("RETURNED_TO_SITE", kit1.Status);

        var kit2 = inventory.Find(k => k.KitNumber == "KIT-002");
        Assert.NotNull(kit2);
        Assert.Equal("QUARANTINED_FOR_DESTRUCTION", kit2.Status);
    }

    [Fact]
    public async Task ValidateScannedBarcodeAsync_DetectsLotMismatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new IpKitApplicationService(_dbContext, _clock, Protector);

        await service.SeedSiteInventoryAsync("STUDY-101", "SITE-01", new List<IpKitSeedItem>
        {
            new("KIT-999", "LOT-EXPECTED-100", _clock.UtcNow.AddMonths(6), "ACTIVE_AT_SITE")
        }, ct);

        var validateMatch = await service.ValidateScannedBarcodeAsync(new IpKitScanValidateRequest("KIT-999", "STUDY-101", "SITE-01", "LOT-EXPECTED-100"), ct);
        Assert.True(validateMatch.IsLotMatch);
        Assert.True(validateMatch.IsValid);

        var validateMismatch = await service.ValidateScannedBarcodeAsync(new IpKitScanValidateRequest("KIT-999", "STUDY-101", "SITE-01", "LOT-MISMATCHED-999"), ct);
        Assert.False(validateMismatch.IsLotMatch);
        Assert.False(validateMismatch.IsValid);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-003")]
    public async Task SeedSiteInventoryAsync_NormalizesStatusToActiveAtSite_AndEncryptsTreatmentArmForAssignment()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new IpKitApplicationService(_dbContext, _clock, Protector);

        var seeded = await service.SeedSiteInventoryAsync("STUDY-101", "SITE-01", new List<IpKitSeedItem>
        {
            new("KIT-ARM-A", "LOT-101", _clock.UtcNow.AddMonths(12), Status: "AVAILABLE", TreatmentArm: "ZEBRA-ARM"),
            new("KIT-UNLABELLED", "LOT-101", _clock.UtcNow.AddMonths(12), Status: "AVAILABLE", TreatmentArm: null),
        }, ct);

        Assert.All(seeded, item => Assert.Equal("ACTIVE_AT_SITE", item.Status));

        var allBeforeReseed = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _dbContext.IpKitInventoryItems, ct);
        var assignableBeforeReseed = allBeforeReseed
            .Where(i =>
                i.StudyId == "STUDY-101"
                && i.SiteId == "SITE-01"
                && i.Status == Cadence.Apps.Execution.Domain.Supply.IpKitInventoryItem.ActiveAtSiteStatus
                && i.AssignedRandomizationId is null
                && i.TreatmentArmCiphertext is not null
                && i.ExpirationDate > _clock.UtcNow)
            .ToList();
        Assert.Single(assignableBeforeReseed);
        Assert.Equal("KIT-ARM-A", assignableBeforeReseed[0].KitNumber);
        Assert.NotNull(assignableBeforeReseed[0].TreatmentArmCiphertext);
        Assert.DoesNotContain("ZEBRA-ARM", assignableBeforeReseed[0].TreatmentArmCiphertext!);
        Assert.Equal(
            "ZEBRA-ARM",
            Protector.Reveal(
                assignableBeforeReseed[0].TreatmentArmCiphertext!,
                Cadence.Apps.Execution.Domain.Supply.IpKitInventoryItem.ArmPurpose("STUDY-101", "KIT-ARM-A")));

        await service.SeedSiteInventoryAsync("STUDY-101", "SITE-01", new List<IpKitSeedItem>
        {
            new("KIT-UNLABELLED", "LOT-101", _clock.UtcNow.AddMonths(12), Status: "ACTIVE_AT_SITE", TreatmentArm: "OKAPI-ARM"),
        }, ct);

        var allAfterReseed = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _dbContext.IpKitInventoryItems, ct);
        var assignableAfterReseed = allAfterReseed
            .Where(i =>
                i.StudyId == "STUDY-101"
                && i.SiteId == "SITE-01"
                && i.Status == Cadence.Apps.Execution.Domain.Supply.IpKitInventoryItem.ActiveAtSiteStatus
                && i.AssignedRandomizationId is null
                && i.TreatmentArmCiphertext is not null
                && i.ExpirationDate > _clock.UtcNow)
            .ToList();
        Assert.Equal(2, assignableAfterReseed.Count);
    }
}
