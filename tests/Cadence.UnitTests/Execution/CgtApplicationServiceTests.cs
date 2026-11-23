using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Supply;
using Cadence.Kernel.Time;
using Cadence.UnitTests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution;

public sealed class CgtApplicationServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ExecutionDbContext _dbContext;
    private readonly IClock _clock;

    public CgtApplicationServiceTests()
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
    public async Task InitializeApheresisAsync_CreatesChainOfCustodyAndHarvestMilestone()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var request = new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "USR-A",
            CustodianName: "Alice Nurse",
            CustodianRole: "Apheresis Specialist");

        var response = await service.InitializeApheresisAsync(request, ct);

        Assert.NotNull(response);
        Assert.Equal("STUDY-CGT-001", response.StudyId);
        Assert.Equal("SUB-1001", response.SubjectId);
        Assert.Equal("HARVESTED", response.Status);
        Assert.Equal("APHERESIS", response.CurrentStage);
        Assert.True(Isbt128Engine.ValidateDin(response.Din));

        var msList = await _dbContext.CGTMilestoneEvents.ToListAsync(ct);
        Assert.Single(msList);
        Assert.Equal("HARVEST_COMPLETED", msList[0].MilestoneType);
    }

    [Fact]
    public async Task InitializeApheresisAsync_ThrowsOnInvalidDin()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var request = new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: "BAD_DIN_123");

        await Assert.ThrowsAsync<InvalidDinException>(() => service.InitializeApheresisAsync(request, ct));
    }

    [Fact]
    public async Task RecordMilestoneAsync_DetectsTemperatureExcursion()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001"), ct);

        var milestoneReq = new CgtMilestoneCreateRequest(
            StudyId: "STUDY-CGT-001",
            ChainOfCustodyId: cocResponse.Id,
            Stage: "TRANSIT",
            MilestoneType: "COURIER_PICKUP",
            TemperatureCelsius: -50.0, // Excursion (> -70°C)
            CustodianId: "USR-COURIER");

        var milestoneResp = await service.RecordMilestoneAsync(milestoneReq, ct);

        Assert.NotNull(milestoneResp);
        Assert.Equal("EXCURSION", milestoneResp.TemperatureStatus);

        var coc = await _dbContext.CGTChainsOfCustody.FirstAsync(x => x.Id == cocResponse.Id, ct);
        Assert.Equal("QUARANTINED", coc.Status);
        Assert.Equal(1, coc.CryoTempExcursionCount);
    }

    [Fact]
    public async Task VerifyBedsideAsync_RejectsDuplicateVerifiers()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var request = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: "=W12342698765400",
            BatchId: null,
            BagBarcodePayload: "DUMMY",
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "USR-SAME",
            SecondaryVerifierId: "USR-SAME"); // Duplicate verifiers!

        var ex = await Assert.ThrowsAsync<DuplicateVerifierException>(() => service.VerifyBedsideAsync(request, ct));
        Assert.Contains("two distinct staff members", ex.Message);
    }

    [Fact]
    public async Task VerifyBedsideAsync_SucceedsWithDualVerifiersAndValidBarcode()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001"), ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88");

        var request = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            PrimarySignatureToken: "SIG-01",
            SecondaryVerifierId: "NURSE-02",
            SecondarySignatureToken: "SIG-02");

        var response = await service.VerifyBedsideAsync(request, ct);

        Assert.NotNull(response);
        Assert.True(response.IsMatch);
        Assert.NotEmpty(response.UnlockToken);

        var coc = await _dbContext.CGTChainsOfCustody.FirstAsync(x => x.Id == cocResponse.Id, ct);
        Assert.Equal("BEDSIDE_VERIFIED", coc.Status);
        Assert.Equal(response.UnlockToken, coc.UnlockToken);
    }

    [Fact]
    public async Task ExecuteInfusionAsync_RequiresValidUnlockToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001"), ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88");

        var verifyResponse = await service.VerifyBedsideAsync(new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "NURSE-02"), ct);

        var infusionReq = new CgtInfusionExecuteRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            ChainOfCustodyId: cocResponse.Id,
            UnlockToken: verifyResponse.UnlockToken,
            PrimaryVerifierId: "NURSE-01");

        var infusionResp = await service.ExecuteInfusionAsync(infusionReq, ct);

        Assert.NotNull(infusionResp);
        Assert.Equal("INFUSED", infusionResp.Status);
        Assert.NotNull(infusionResp.InfusedAt);
    }

    [Fact]
    public async Task GenerateDestructionCertificateAsync_RejectsDuplicateWitnesses()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var request = new CgtDestructionCertificateCreateRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            LotNumber: "LOT-99",
            KitNumbers: ["KIT-01"],
            VialIdentifiers: ["VIAL-01"],
            DestructionReason: "Expired",
            DestructionMethod: "Autoclave",
            DestructionFacility: "Site Pharmacy Biohazard Room",
            Witness1Id: "PHARM-SAME",
            Witness2Id: "PHARM-SAME"); // Duplicate witnesses!

        var ex = await Assert.ThrowsAsync<DuplicateWitnessException>(() => service.GenerateDestructionCertificateAsync(request, ct));
        Assert.Contains("Witness 1", ex.Message);
    }

    [Fact]
    public async Task GenerateDestructionCertificateAsync_SucceedsWithDualWitnesses()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new CgtApplicationService(_dbContext, _clock);

        var request = new CgtDestructionCertificateCreateRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            LotNumber: "LOT-99",
            KitNumbers: ["KIT-01"],
            VialIdentifiers: ["VIAL-01"],
            DestructionReason: "Expired",
            DestructionMethod: "Autoclave",
            DestructionFacility: "Site Pharmacy Biohazard Room",
            Witness1Id: "PHARM-01",
            Witness1SignatureToken: "SIG-PHARM1",
            Witness2Id: "PHARM-02",
            Witness2SignatureToken: "SIG-PHARM2");

        var response = await service.GenerateDestructionCertificateAsync(request, ct);

        Assert.NotNull(response);
        Assert.StartsWith("COD-", response.CertificateNumber);
        Assert.NotEmpty(response.CryptographicSeal);
        Assert.Equal("COMPLETED", response.Status);
    }
}
