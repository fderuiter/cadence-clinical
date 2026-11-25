using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain.Supply;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.UnitTests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Execution;

public sealed class CgtApplicationServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ExecutionDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ReplayCache _replayCache;
    private readonly SigTokenVerifier _sigTokenVerifier;

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
        _replayCache = new ReplayCache(_clock);
        _sigTokenVerifier = new SigTokenVerifier(_replayCache, _clock);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private CgtApplicationService CreateService(ISigTokenUseStore? tokenStore = null) =>
        new(
            _dbContext,
            _clock,
            Options.Create(new CgtBarcodeProtectionOptions { KeyBase64 = TestSecurityContext.CgtBarcodeKeyBase64 }),
            tokenStore is null ? _sigTokenVerifier : new SigTokenVerifier(tokenStore, _clock),
            TestSecurityContext.GatewayOptions());

    private static string CreateBedsideToken(
        string userId,
        CgtBedsideVerificationRequest request,
        string[]? roles = null,
        string? overrideBatchId = null) =>
        TestSecurityContext.CreateSigToken(
            userId: userId,
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: roles ?? ["Site CRC"],
            batchId: overrideBatchId ?? request.TargetRecordBindingId());

    private static string CreateDestructionToken(
        string userId,
        CgtDestructionCertificateCreateRequest request,
        string[]? roles = null,
        string? overrideBatchId = null) =>
        TestSecurityContext.CreateSigToken(
            userId: userId,
            action: CgtApplicationService.DestructionCertificateAction,
            semanticAction: CgtApplicationService.DestructionCertificateSemanticAction,
            roles: roles ?? ["Pharmacist"],
            batchId: overrideBatchId ?? request.TargetRecordBindingId());

    internal static string CreateForgedAuditorToken(
        string userId,
        string action,
        string semanticAction,
        string batchId)
    {
        var now = SystemClock.Instance.UtcNow.UtcDateTime;
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId)]),
            Claims = new Dictionary<string, object>
            {
                [SigTokenClaims.Username] = userId,
                [SigTokenClaims.Roles] = new[] { "Auditor" },
                [SigTokenClaims.Action] = action,
                [SigTokenClaims.SemanticAction] = semanticAction,
                [SigTokenClaims.BatchId] = batchId,
                [SigTokenClaims.Version] = SigTokenClaims.CurrentVersion,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(SigTokenClaims.Lifetime),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256),
        }));
    }

    [Fact]
    public async Task InitializeApheresisAsync_CreatesChainOfCustodyAndHarvestMilestone()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var request = new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "USR-A",
            CustodianName: "Alice Nurse",
            CustodianRole: "Apheresis Specialist");

        var response = await service.InitializeApheresisAsync(request, "USR-A", ct);

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
        var service = CreateService();

        var request = new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: "BAD_DIN_123");

        await Assert.ThrowsAsync<InvalidDinException>(() => service.InitializeApheresisAsync(request, "USR-A", ct));
    }

    [Fact]
    public async Task InitializeApheresisAsync_WithoutCustodianOrAuditActor_ThrowsAuditActorUnresolvedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var request = new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001");

        await Assert.ThrowsAsync<AuditActorUnresolvedException>(
            () => service.InitializeApheresisAsync(request, callerUserId: null, ct));
    }

    [Fact]
    public async Task RecordMilestoneAsync_DetectsTemperatureExcursion()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "USR-A"), "USR-A", ct);

        var milestoneReq = new CgtMilestoneCreateRequest(
            StudyId: "STUDY-CGT-001",
            ChainOfCustodyId: cocResponse.Id,
            Stage: "TRANSIT",
            MilestoneType: "COURIER_PICKUP",
            TemperatureCelsius: -50.0, // Excursion (> -70°C)
            CustodianId: "USR-COURIER");

        var milestoneResp = await service.RecordMilestoneAsync(milestoneReq, "USR-COURIER", ct);

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
        var service = CreateService();

        var request = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: "=W12342698765400",
            BatchId: null,
            BagBarcodePayload: "DUMMY",
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "USR-SAME",
            SecondaryVerifierId: "USR-SAME");

        var ex = await Assert.ThrowsAsync<DuplicateVerifierException>(() => service.VerifyBedsideAsync(request, "USR-SAME", ct));
        Assert.Contains("two distinct staff members", ex.Message);
    }

    [Fact]
    public async Task VerifyBedsideAsync_RejectsCallerMismatchAndProtectsPrimaryTokenWhenSecondaryFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "NURSE-01"), "NURSE-01", ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88");

        var baseRequest = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "PI-02");

        // 1. Caller identity mismatch must fail closed.
        await Assert.ThrowsAsync<CallerIdentityMismatchException>(
            () => service.VerifyBedsideAsync(baseRequest, "IMPERSONATED-CALLER", ct));

        // 2. Unverified arbitrary strings must fail step-up verification.
        var fakeTokenRequest = baseRequest with
        {
            PrimarySignatureToken = "unverified-token-01",
            SecondarySignatureToken = "unverified-token-02",
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.VerifyBedsideAsync(fakeTokenRequest, "NURSE-01", ct));

        // 3. Auditors cannot obtain issued signature tokens, and if a forged auditor token or wrong target record
        //    is presented as the secondary token, the valid primary token's jti must NOT be consumed.
        Assert.Throws<InvalidOperationException>(() => CreateBedsideToken("PI-02", baseRequest, roles: ["Auditor"]));
        string validPrimaryToken = CreateBedsideToken("NURSE-01", baseRequest);
        string auditorSecondaryToken = CreateForgedAuditorToken(
            "PI-02",
            CgtApplicationService.BedsideVerifyAction,
            CgtApplicationService.BedsideVerifySemanticAction,
            baseRequest.TargetRecordBindingId());
        string wrongRecordSecondaryToken = CreateBedsideToken("PI-02", baseRequest, overrideBatchId: "OTHER-STUDY:OTHER-SUB:DIN:BATCH");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.VerifyBedsideAsync(
                baseRequest with
                {
                    PrimarySignatureToken = validPrimaryToken,
                    SecondarySignatureToken = auditorSecondaryToken,
                },
                "NURSE-01",
                ct));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.VerifyBedsideAsync(
                baseRequest with
                {
                    PrimarySignatureToken = validPrimaryToken,
                    SecondarySignatureToken = wrongRecordSecondaryToken,
                },
                "NURSE-01",
                ct));

        // Retrying with the SAME unburned primary token and a valid secondary token must succeed.
        string validSecondaryToken = CreateBedsideToken("PI-02", baseRequest, roles: ["Site Investigator"]);
        var verified = await service.VerifyBedsideAsync(
            baseRequest with
            {
                PrimarySignatureToken = validPrimaryToken,
                SecondarySignatureToken = validSecondaryToken,
            },
            "NURSE-01",
            ct);

        Assert.True(verified.IsMatch);

        // 4. Replaying either consumed token must be rejected, and replaying secondary must NOT burn a fresh primary token.
        string secondAttemptPrimaryToken = CreateBedsideToken("NURSE-01", baseRequest);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.VerifyBedsideAsync(
                baseRequest with
                {
                    PrimarySignatureToken = secondAttemptPrimaryToken,
                    SecondarySignatureToken = validSecondaryToken,
                },
                "NURSE-01",
                ct));

        string freshSecondaryToken = CreateBedsideToken("PI-02", baseRequest, roles: ["Site Investigator"]);
        var secondVerified = await service.VerifyBedsideAsync(
            baseRequest with
            {
                PrimarySignatureToken = secondAttemptPrimaryToken,
                SecondarySignatureToken = freshSecondaryToken,
            },
            "NURSE-01",
            ct);
        Assert.True(secondVerified.IsMatch);
    }

    [Fact]
    public async Task VerifyBedsideAsync_DurableStore_DoesNotBurnPrimaryWhenSecondaryWasAlreadyConsumed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sigTokenConnection = new SqliteConnection("DataSource=:memory:");
        await sigTokenConnection.OpenAsync(ct);

        var sigDbOptions = new DbContextOptionsBuilder<SqliteSigTokenDbContext>()
            .UseSqlite(sigTokenConnection)
            .Options;
        await using (var schemaDb = new SqliteSigTokenDbContext(sigDbOptions))
        {
            await schemaDb.Database.EnsureCreatedAsync(ct);
        }

        await using var serviceProvider = new ServiceCollection()
            .AddScoped(_ => new SqliteSigTokenDbContext(sigDbOptions))
            .BuildServiceProvider();

        var durableStore = new PostgresSigTokenUseStore<SqliteSigTokenDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _clock);
        var service = CreateService(durableStore);

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-DURABLE-01",
            CustodianId: "NURSE-01"), "NURSE-01", ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-DURABLE-01",
            din: cocResponse.Din,
            batchId: "BATCH-99");

        var baseRequest = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-DURABLE-01",
            Din: cocResponse.Din,
            BatchId: "BATCH-99",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-DURABLE-01",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "PI-02");

        string initialPrimary = CreateBedsideToken("NURSE-01", baseRequest);
        string consumedSecondary = CreateBedsideToken("PI-02", baseRequest, roles: ["Site Investigator"]);
        await service.VerifyBedsideAsync(
            baseRequest with { PrimarySignatureToken = initialPrimary, SecondarySignatureToken = consumedSecondary },
            "NURSE-01",
            ct);

        string freshPrimary = CreateBedsideToken("NURSE-01", baseRequest);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.VerifyBedsideAsync(
                baseRequest with { PrimarySignatureToken = freshPrimary, SecondarySignatureToken = consumedSecondary },
                "NURSE-01",
                ct));

        string freshSecondary = CreateBedsideToken("PI-02", baseRequest, roles: ["Site Investigator"]);
        var retryResult = await service.VerifyBedsideAsync(
            baseRequest with { PrimarySignatureToken = freshPrimary, SecondarySignatureToken = freshSecondary },
            "NURSE-01",
            ct);
        Assert.True(retryResult.IsMatch);
    }

    [Fact]
    public async Task VerifyBedsideAsync_RejectsBarcodeSignedWithUnconfiguredKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "NURSE-01"), "NURSE-01", ct);

        byte[] unconfiguredKey = new byte[CgtBarcodeEngine.SecretKeySizeBytes];
        unconfiguredKey.AsSpan().Fill(0xAB);

        string forgedBarcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88",
            secretKey: unconfiguredKey);

        var request = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: forgedBarcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "NURSE-02");

        var signedRequest = request with
        {
            PrimarySignatureToken = CreateBedsideToken("NURSE-01", request),
            SecondarySignatureToken = CreateBedsideToken("NURSE-02", request, roles: ["Site Investigator"]),
        };

        await Assert.ThrowsAsync<BarcodeVerificationException>(() => service.VerifyBedsideAsync(signedRequest, "NURSE-01", ct));
    }

    [Fact]
    public async Task VerifyBedsideAsync_SucceedsWithDualVerifiersAndValidBarcode()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "USR-APHERESIS"), "USR-APHERESIS", ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88");

        var baseRequest = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "NURSE-02");

        var request = baseRequest with
        {
            PrimarySignatureToken = CreateBedsideToken("NURSE-01", baseRequest),
            SecondarySignatureToken = CreateBedsideToken("NURSE-02", baseRequest, roles: ["Site Investigator"]),
        };

        var response = await service.VerifyBedsideAsync(request, "NURSE-01", ct);

        Assert.NotNull(response);
        Assert.True(response.IsMatch);
        Assert.NotEmpty(response.UnlockToken);

        var coc = await _dbContext.CGTChainsOfCustody.FirstAsync(x => x.Id == cocResponse.Id, ct);
        Assert.Equal("BEDSIDE_VERIFIED", coc.Status);
        Assert.Equal(response.UnlockToken, coc.UnlockToken);
    }

    [Fact]
    public async Task ExecuteInfusionAsync_RequiresValidUnlockTokenAndMatchingCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var cocResponse = await service.InitializeApheresisAsync(new CgtApheresisCreateRequest(
            StudyId: "STUDY-CGT-001",
            CollectionSiteId: "SITE-101",
            SubjectId: "SUB-1001",
            CustodianId: "USR-APHERESIS"), "USR-APHERESIS", ct);

        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT-001",
            siteId: "SITE-101",
            subjectId: "SUB-1001",
            din: cocResponse.Din,
            batchId: "BATCH-88");

        var bedsideBase = new CgtBedsideVerificationRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            Din: cocResponse.Din,
            BatchId: "BATCH-88",
            BagBarcodePayload: barcodePayload,
            PatientWristbandId: "SUB-1001",
            PrimaryVerifierId: "NURSE-01",
            SecondaryVerifierId: "NURSE-02");

        var verifyResponse = await service.VerifyBedsideAsync(
            bedsideBase with
            {
                PrimarySignatureToken = CreateBedsideToken("NURSE-01", bedsideBase),
                SecondarySignatureToken = CreateBedsideToken("NURSE-02", bedsideBase, roles: ["Site Investigator"]),
            },
            "NURSE-01",
            ct);

        var infusionReq = new CgtInfusionExecuteRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            SubjectId: "SUB-1001",
            ChainOfCustodyId: cocResponse.Id,
            UnlockToken: verifyResponse.UnlockToken,
            PrimaryVerifierId: "NURSE-01");

        await Assert.ThrowsAsync<CallerIdentityMismatchException>(
            () => service.ExecuteInfusionAsync(infusionReq, "OTHER-USER", ct));

        var infusionResp = await service.ExecuteInfusionAsync(infusionReq, "NURSE-01", ct);

        Assert.NotNull(infusionResp);
        Assert.Equal("INFUSED", infusionResp.Status);
        Assert.NotNull(infusionResp.InfusedAt);
    }

    [Fact]
    public async Task GenerateDestructionCertificateAsync_RejectsDuplicateWitnesses()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

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
            Witness2Id: "PHARM-SAME");

        var ex = await Assert.ThrowsAsync<DuplicateWitnessException>(
            () => service.GenerateDestructionCertificateAsync(request, "PHARM-SAME", ct));
        Assert.Contains("Witness 1", ex.Message);
    }

    [Fact]
    public async Task GenerateDestructionCertificateAsync_EnforcesCallerAndDualWitnessTokens_WithoutBurningWitness1()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var baseRequest = new CgtDestructionCertificateCreateRequest(
            StudyId: "STUDY-CGT-001",
            SiteId: "SITE-101",
            LotNumber: "LOT-99",
            KitNumbers: ["KIT-01"],
            VialIdentifiers: ["VIAL-01"],
            DestructionReason: "Expired",
            DestructionMethod: "Autoclave",
            DestructionFacility: "Site Pharmacy Biohazard Room",
            Witness1Id: "PHARM-01",
            Witness2Id: "PHARM-02");

        await Assert.ThrowsAsync<CallerIdentityMismatchException>(
            () => service.GenerateDestructionCertificateAsync(baseRequest, "IMPERSONATED-PHARM", ct));

        string witness1Token = CreateDestructionToken("PHARM-01", baseRequest);
        string auditorWitness2Token = CreateForgedAuditorToken(
            "PHARM-02",
            CgtApplicationService.DestructionCertificateAction,
            CgtApplicationService.DestructionCertificateSemanticAction,
            baseRequest.TargetRecordBindingId());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GenerateDestructionCertificateAsync(
                baseRequest with
                {
                    Witness1SignatureToken = witness1Token,
                    Witness2SignatureToken = auditorWitness2Token,
                },
                "PHARM-01",
                ct));

        // witness1Token was not burned when witness2Token failed; succeeds with valid witness2Token.
        string validWitness2Token = CreateDestructionToken("PHARM-02", baseRequest, roles: ["Site Investigator"]);
        var response = await service.GenerateDestructionCertificateAsync(
            baseRequest with
            {
                Witness1SignatureToken = witness1Token,
                Witness2SignatureToken = validWitness2Token,
            },
            "PHARM-01",
            ct);

        Assert.NotNull(response);
        Assert.StartsWith("COD-", response.CertificateNumber);
        Assert.NotEmpty(response.CryptographicSeal);
        Assert.Equal("COMPLETED", response.Status);
    }

    private sealed class SqliteSigTokenDbContext(DbContextOptions<SqliteSigTokenDbContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());
        }
    }
}

public sealed class CgtEndpointsSecurityTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    [Theory]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void HostStartup_WithoutConfiguredCgtBarcodeKey_FailsValidation(string invalidKeyBase64)
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey()
            .UseTestRandomizationKey()
            .UseSetting(CgtBarcodeProtectionOptions.ConfigurationKey, invalidKeyBase64));

        var ex = Assert.IsType<OptionsValidationException>(unconfigured.ValidateStartup());
        Assert.Contains(CgtBarcodeProtectionOptions.ConfigurationKey, ex.Message);
    }

    [Fact]
    public async Task BedsideVerifyEndpoint_AfterApheresisInit_RejectsImpersonatedCallerUnverifiedTokensAndUnconfiguredBarcode()
    {
        var ct = TestContext.Current.CancellationToken;
        var nurseIdentity = TestSecurityContext.Identity("nurse-01", "Site CRC") with
        {
            SiteIds = ["SITE-101"],
            ChangeReason = "Apheresis and bedside dual verification",
        };
        using HttpClient client = factory.CreateSignedClient(nurseIdentity);

        using var apheresisResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/apheresis",
            new
            {
                study_id = "STUDY-VERIFY-01",
                collection_site_id = "SITE-101",
                subject_id = "SUBJ-VERIFY-01",
                custodian_id = "nurse-01",
                custodian_name = "Nurse One",
                custodian_role = "Site CRC",
            },
            ct);
        Assert.Equal(HttpStatusCode.OK, apheresisResp.StatusCode);

        using var apheresisDoc = JsonDocument.Parse(await apheresisResp.Content.ReadAsStringAsync(ct));
        string apheresisDin = apheresisDoc.RootElement.GetProperty("din").GetString()!;

        byte[] unconfiguredKey = new byte[CgtBarcodeEngine.SecretKeySizeBytes];
        unconfiguredKey.AsSpan().Fill(0x42);
        string unconfiguredKeyBarcode = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-VERIFY-01",
            siteId: "SITE-101",
            subjectId: "SUBJ-VERIFY-01",
            din: apheresisDin,
            batchId: "BATCH-VERIFY-01",
            secretKey: unconfiguredKey);

        // Missing body tokens trigger endpoint electronic-signature gate -> 401 REAUTHENTICATION_REQUIRED
        using var missingTokensResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-VERIFY-01",
                site_id = "SITE-101",
                subject_id = "SUBJ-VERIFY-01",
                din = apheresisDin,
                batch_id = "BATCH-VERIFY-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-VERIFY-01",
                primary_verifier_id = "nurse-01",
                secondary_verifier_id = "nurse-02",
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missingTokensResp.StatusCode);
        AssertReauthenticationRequired(await missingTokensResp.Content.ReadAsStringAsync(ct));

        // Impersonated primary_verifier_id -> 403 SIGNER_MISMATCH
        using var spoofedCallerResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-VERIFY-01",
                site_id = "SITE-101",
                subject_id = "SUBJ-VERIFY-01",
                din = apheresisDin,
                batch_id = "BATCH-VERIFY-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-VERIFY-01",
                primary_verifier_id = "impersonated-nurse-01",
                primary_signature_token = "unverified-token-01",
                secondary_verifier_id = "impersonated-nurse-02",
                secondary_signature_token = "unverified-token-02",
            },
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, spoofedCallerResp.StatusCode);
        Assert.Contains("SIGNER_MISMATCH", await spoofedCallerResp.Content.ReadAsStringAsync(ct));

        // Matching primary_verifier_id with unverified signature tokens -> 401 REAUTHENTICATION_REQUIRED
        using var unverifiedSigResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-VERIFY-01",
                site_id = "SITE-101",
                subject_id = "SUBJ-VERIFY-01",
                din = apheresisDin,
                batch_id = "BATCH-VERIFY-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-VERIFY-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = "unverified-token-01",
                secondary_verifier_id = "impersonated-nurse-02",
                secondary_signature_token = "unverified-token-02",
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedSigResp.StatusCode);
        AssertReauthenticationRequired(await unverifiedSigResp.Content.ReadAsStringAsync(ct));

        // Valid dual step-up tokens with unconfigured-key barcode -> 400 BadRequest (cryptographic barcode rejection)
        string targetBinding = $"STUDY-VERIFY-01:SUBJ-VERIFY-01:{apheresisDin}:BATCH-VERIFY-01";
        string primaryToken = TestSecurityContext.CreateSigToken(
            userId: "nurse-01",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site CRC"],
            batchId: targetBinding);
        string secondaryToken = TestSecurityContext.CreateSigToken(
            userId: "nurse-02",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site Investigator"],
            batchId: targetBinding);

        using var badKeyResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-VERIFY-01",
                site_id = "SITE-101",
                subject_id = "SUBJ-VERIFY-01",
                din = apheresisDin,
                batch_id = "BATCH-VERIFY-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-VERIFY-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = primaryToken,
                secondary_verifier_id = "nurse-02",
                secondary_signature_token = secondaryToken,
            },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, badKeyResp.StatusCode);
        Assert.Contains("Cryptographic signature verification failed", await badKeyResp.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task BedsideVerifyEndpoint_EnforcesCallerMatchDualStepUpTokensAndConfiguredBarcodeKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var nurseIdentity = TestSecurityContext.Identity("nurse-01", "Site CRC") with
        {
            SiteIds = ["SITE-01"],
            ChangeReason = "Bedside dual verification",
        };
        using HttpClient client = factory.CreateSignedClient(nurseIdentity);

        string din = "=W12342698765400";
        string validBarcode = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT",
            siteId: "SITE-01",
            subjectId: "SUBJ-01",
            din: din,
            batchId: "BATCH-01");

        byte[] unconfiguredKey = new byte[CgtBarcodeEngine.SecretKeySizeBytes];
        unconfiguredKey.AsSpan().Fill(0x5A);
        string unconfiguredKeyBarcode = CgtBarcodeTestSigner.SignPayload(
            studyId: "STUDY-CGT",
            siteId: "SITE-01",
            subjectId: "SUBJ-01",
            din: din,
            batchId: "BATCH-01",
            secretKey: unconfiguredKey);

        // 1. Impersonated primary_verifier_id -> 403 Forbidden (SIGNER_MISMATCH)
        using var impersonatedResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "impersonated-nurse-01",
                primary_signature_token = "unverified-token-01",
                secondary_verifier_id = "pi-02",
                secondary_signature_token = "unverified-token-02",
            },
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, impersonatedResp.StatusCode);
        Assert.Contains("SIGNER_MISMATCH", await impersonatedResp.Content.ReadAsStringAsync(ct));

        // 2. Unverified tokens -> 401 Unauthorized (REAUTHENTICATION_REQUIRED)
        using var unverifiedResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = validBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = "unverified-token-01",
                secondary_verifier_id = "pi-02",
                secondary_signature_token = "unverified-token-02",
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedResp.StatusCode);
        AssertReauthenticationRequired(await unverifiedResp.Content.ReadAsStringAsync(ct));

        // 3. Auditor secondary token -> 401 REAUTHENTICATION_REQUIRED without burning primary token
        string targetBinding = $"STUDY-CGT:SUBJ-01:{din}:BATCH-01";
        string primaryToken = TestSecurityContext.CreateSigToken(
            userId: "nurse-01",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site CRC"],
            batchId: targetBinding);
        string auditorSecondaryToken = CgtApplicationServiceTests.CreateForgedAuditorToken(
            userId: "pi-02",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            batchId: targetBinding);

        using var auditorResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = validBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = primaryToken,
                secondary_verifier_id = "pi-02",
                secondary_signature_token = auditorSecondaryToken,
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, auditorResp.StatusCode);
        AssertReauthenticationRequired(await auditorResp.Content.ReadAsStringAsync(ct));

        // 4. Primary token was NOT burned: retry with valid secondary token + unconfigured-key barcode -> 400 BadRequest
        string secondaryTokenForForgedBarcode = TestSecurityContext.CreateSigToken(
            userId: "pi-02",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site Investigator"],
            batchId: targetBinding);

        using var forgedBarcodeResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = unconfiguredKeyBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = primaryToken,
                secondary_verifier_id = "pi-02",
                secondary_signature_token = secondaryTokenForForgedBarcode,
            },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, forgedBarcodeResp.StatusCode);

        // 5. Replaying those now-consumed tokens -> 401 REAUTHENTICATION_REQUIRED
        using var replayResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = validBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = primaryToken,
                secondary_verifier_id = "pi-02",
                secondary_signature_token = secondaryTokenForForgedBarcode,
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        AssertReauthenticationRequired(await replayResp.Content.ReadAsStringAsync(ct));

        // 6. Fresh dual step-up tokens + barcode signed with configured CGT_BARCODE_HMAC_KEY -> 200 OK
        string freshPrimary = TestSecurityContext.CreateSigToken(
            userId: "nurse-01",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site CRC"],
            batchId: targetBinding);
        string freshSecondary = TestSecurityContext.CreateSigToken(
            userId: "pi-02",
            action: CgtApplicationService.BedsideVerifyAction,
            semanticAction: CgtApplicationService.BedsideVerifySemanticAction,
            roles: ["Site Investigator"],
            batchId: targetBinding);

        using var okResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/bedside-verify",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                subject_id = "SUBJ-01",
                din,
                batch_id = "BATCH-01",
                bag_barcode_payload = validBarcode,
                patient_wristband_id = "SUBJ-01",
                primary_verifier_id = "nurse-01",
                primary_signature_token = freshPrimary,
                secondary_verifier_id = "pi-02",
                secondary_signature_token = freshSecondary,
            },
            ct);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
    }

    [Fact]
    public async Task DestructionCertificateEndpoint_EnforcesCallerMatchAndDualWitnessTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        var pharmIdentity = TestSecurityContext.Identity("pharm-01", "Pharmacist") with
        {
            SiteIds = ["SITE-01"],
            ChangeReason = "Product destruction certificate",
        };
        using HttpClient client = factory.CreateSignedClient(pharmIdentity);

        string targetBinding = "STUDY-CGT:SITE-01:LOT-77:";

        using var mismatchResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/destruction-certificate",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                lot_number = "LOT-77",
                kit_numbers = new[] { "KIT-01" },
                vial_identifiers = new[] { "VIAL-01" },
                destruction_reason = "Residual disposal",
                destruction_method = "AUTOCLAVE",
                destruction_facility = "Site Pharmacy",
                witness_1_id = "impersonated-pharm",
                witness_1_signature_token = "fake-1",
                witness_2_id = "pi-02",
                witness_2_signature_token = "fake-2",
            },
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchResp.StatusCode);
        Assert.Contains("SIGNER_MISMATCH", await mismatchResp.Content.ReadAsStringAsync(ct));

        string witness1Token = TestSecurityContext.CreateSigToken(
            userId: "pharm-01",
            action: CgtApplicationService.DestructionCertificateAction,
            semanticAction: CgtApplicationService.DestructionCertificateSemanticAction,
            roles: ["Pharmacist"],
            batchId: targetBinding);
        string auditorWitness2Token = CgtApplicationServiceTests.CreateForgedAuditorToken(
            userId: "pi-02",
            action: CgtApplicationService.DestructionCertificateAction,
            semanticAction: CgtApplicationService.DestructionCertificateSemanticAction,
            batchId: targetBinding);

        using var auditorResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/destruction-certificate",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                lot_number = "LOT-77",
                kit_numbers = new[] { "KIT-01" },
                vial_identifiers = new[] { "VIAL-01" },
                destruction_reason = "Residual disposal",
                destruction_method = "AUTOCLAVE",
                destruction_facility = "Site Pharmacy",
                witness_1_id = "pharm-01",
                witness_1_signature_token = witness1Token,
                witness_2_id = "pi-02",
                witness_2_signature_token = auditorWitness2Token,
            },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, auditorResp.StatusCode);
        AssertReauthenticationRequired(await auditorResp.Content.ReadAsStringAsync(ct));

        // witness1Token remains unconsumed and succeeds with a valid witness2Token.
        string validWitness2Token = TestSecurityContext.CreateSigToken(
            userId: "pi-02",
            action: CgtApplicationService.DestructionCertificateAction,
            semanticAction: CgtApplicationService.DestructionCertificateSemanticAction,
            roles: ["Site Investigator"],
            batchId: targetBinding);

        using var okResp = await client.PostAsJsonAsync(
            "/api/v1/execution/rtsm/cgt/destruction-certificate",
            new
            {
                study_id = "STUDY-CGT",
                site_id = "SITE-01",
                lot_number = "LOT-77",
                kit_numbers = new[] { "KIT-01" },
                vial_identifiers = new[] { "VIAL-01" },
                destruction_reason = "Residual disposal",
                destruction_method = "AUTOCLAVE",
                destruction_facility = "Site Pharmacy",
                witness_1_id = "pharm-01",
                witness_1_signature_token = witness1Token,
                witness_2_id = "pi-02",
                witness_2_signature_token = validWitness2Token,
            },
            ct);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
    }

    private static void AssertReauthenticationRequired(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        Assert.Equal("REAUTHENTICATION_REQUIRED", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("REAUTHENTICATION_REQUIRED", doc.RootElement.GetProperty("detail").GetString());
    }
}
