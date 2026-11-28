using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Verifies that <see cref="GatewayAuthenticationOptions"/> is bound and validated at startup across default and named
/// options, and that CTMS, eTMF, and Payments never fall back to raw <see cref="IConfiguration"/> or process
/// environment variables (ADR-2256, ADR-2260, ADR-2515).
/// </summary>
public sealed class GatewayAuthenticationOptionsBindingTests
{
    private const string OptionsSecretMarker = "test-options-gateway-secret-marker-01"; // pragma: allowlist secret
    private const string RawDivergentMarker = "short-raw-marker"; // pragma: allowlist secret
    private const string StudyId = "CADENCE-GATEWAY-OPT-01";
    private const string SiteId = "SITE-01";
    private const string ChangeReason = "21 CFR Part 11 startup greenlight and eTMF verification";

    private static readonly Lock EnvironmentLock = new();

    [Theory]
    [InlineData("")]
    [InlineData(RawDivergentMarker)]
    [Trait("Category", "GxP_IQ")]
    public void CtmsHostStartup_WithMissingOrShortGatewaySecret_ThrowsOptionsValidationException(string rawSecret)
    {
        using var host = new StartupValidationFactory<Cadence.Apps.Ctms.Program>(builder => builder
            .UseTestPostgresConnectionString()
            .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, rawSecret));

        host.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain("GATEWAY_SECRET");
    }

    [Theory]
    [InlineData("")]
    [InlineData(RawDivergentMarker)]
    [Trait("Category", "GxP_IQ")]
    public void EtmfHostStartup_WithMissingOrShortGatewaySecret_ThrowsOptionsValidationException(string rawSecret)
    {
        using var host = new StartupValidationFactory<Cadence.Apps.Etmf.Program>(builder => builder
            .UseTestPostgresConnectionString()
            .UseTestStorage()
            .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, rawSecret));

        host.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain("GATEWAY_SECRET");
    }

    [Theory]
    [InlineData("")]
    [InlineData(RawDivergentMarker)]
    [Trait("Category", "GxP_IQ")]
    public void PaymentsHostStartup_WithMissingOrShortGatewaySecret_ThrowsOptionsValidationException(string rawSecret)
    {
        using var host = new StartupValidationFactory<Cadence.Apps.Payments.Program>(builder => builder
            .UseTestPostgresConnectionString()
            .UseTestFieldProtectionKeys()
            .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, rawSecret));

        host.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain("GATEWAY_SECRET");
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public async Task AddCadenceSecurity_SynchronizesDefaultAndNamedGatewayOptions_ForTrainingAndKernelLockGuard()
    {
        var ct = TestContext.Current.CancellationToken;
        var rawConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey] = RawDivergentMarker,
            })
            .Build();

        // Case 1: Configuring default unnamed GatewayAuthenticationOptions synchronizes named SchemeName options.
        var defaultConfiguredServices = new ServiceCollection()
            .AddSingleton<IConfiguration>(rawConfig)
            .AddSingleton<IClock>(new FakeClock(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero)));
        defaultConfiguredServices.AddCadenceSecurity();
        defaultConfiguredServices.Configure<GatewayAuthenticationOptions>(o => o.GatewaySecret = OptionsSecretMarker);

        await using (var sp = defaultConfiguredServices.BuildServiceProvider())
        {
            var defaultOptions = sp.GetRequiredService<IOptions<GatewayAuthenticationOptions>>();
            var monitor = sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>();

            defaultOptions.Value.GatewaySecret.Should().Be(OptionsSecretMarker);
            monitor.CurrentValue.GatewaySecret.Should().Be(OptionsSecretMarker);
            monitor.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret.Should().Be(OptionsSecretMarker);

            // Verify HttpKernelLockGuard signs outbound Kernel lock queries with the synchronized secret.
            Dictionary<string, string>? kernelHeaders = null;
            using var kernelClient = new HttpClient(new StubMessageHandler(req =>
            {
                kernelHeaders = CaptureHeaders(req);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { locks = Array.Empty<object>() })
                };
            }))
            {
                BaseAddress = new Uri("https://kernel.internal")
            };

            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = TestSecurityContext.CreateScopedPrincipal("cra-lock-01", [StudyId], [SiteId], "cra"),
            };

            var lockGuard = new HttpKernelLockGuard(
                kernelClient,
                httpContextAccessor,
                monitor,
                sp.GetRequiredService<IClock>());
            var lockResult = await lockGuard.EnsureUnlockedAsync(
                StudyId,
                SiteId,
                subjectCode: null,
                ct);
            lockResult.IsSuccess.Should().BeTrue();
            kernelHeaders.Should().NotBeNull();
            VerifyWebhookSignature(kernelHeaders!, OptionsSecretMarker).Should().BeTrue();
            VerifyWebhookSignature(kernelHeaders!, RawDivergentMarker).Should().BeFalse();

            // Verify TrainingDelegationService resolves the synchronized named scheme secret.
            var knowledgeDbOptions = new DbContextOptionsBuilder<KnowledgeDbContext>()
                .UseInMemoryDatabase($"knowledge-sync-{Guid.NewGuid():N}")
                .Options;
            await using var knowledgeDb = new KnowledgeDbContext(knowledgeDbOptions);
            var trainingService = new TrainingDelegationService(
                knowledgeDb,
                new KnowledgeUnitOfWork(knowledgeDb),
                sp.GetRequiredService<IClock>(),
                monitor);
            trainingService.Should().NotBeNull();
        }

        // Case 2: Configuring named GatewayAuthenticationHandler.SchemeName synchronizes default IOptions<GatewayAuthenticationOptions>.
        var namedConfiguredServices = new ServiceCollection()
            .AddSingleton<IConfiguration>(rawConfig);
        namedConfiguredServices.AddCadenceSecurity();
        namedConfiguredServices.Configure<GatewayAuthenticationOptions>(
            GatewayAuthenticationHandler.SchemeName,
            o => o.GatewaySecret = OptionsSecretMarker);

        await using (var sp = namedConfiguredServices.BuildServiceProvider())
        {
            sp.GetRequiredService<IOptions<GatewayAuthenticationOptions>>().Value.GatewaySecret
                .Should().Be(OptionsSecretMarker);
            sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>()
                .Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret
                .Should().Be(OptionsSecretMarker);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(RawDivergentMarker)]
    [GxPRequirement("PRD-SSU-003")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CtmsCertifyAndBatchCertify_WithDivergentOrEmptyRawConfig_SealTokensWithValidatedOptionsWithoutBurningSigToken(
        string rawConfigSecret)
    {
        var ct = TestContext.Current.CancellationToken;
        string dbName = $"ctms-gateway-options-{Guid.NewGuid():N}";

        await using var factory = new WebApplicationFactory<Cadence.Apps.Ctms.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestPostgresConnectionString()
                .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, rawConfigSecret);

            builder.ConfigureTestServices(services =>
            {
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);
                services.Configure<GatewayAuthenticationOptions>(o => o.GatewaySecret = OptionsSecretMarker);

                services.RemoveAll<DbContextOptions<CtmsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CtmsDbContext>>();
                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                    return accessor;
                });

                services.AddDbContext<CtmsDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(dbName)
                        .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                        .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance);
                    options.UseInternalServiceProvider(TestSupport.TestEfServiceProviders.InMemory);
                });
            });
        });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            foreach (string site in new[] { SiteId, "SITE-02" })
            {
                db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate
                {
                    StudyId = StudyId,
                    SiteId = site,
                    SiteName = $"Site {site}",
                    CountryCode = "US",
                    ReadinessPercentage = 100,
                    FacetRegulatoryIrb = true,
                    FacetCtaBudget = true,
                    FacetEisfBaseline = true,
                    FacetDoaLog = true,
                    FacetStaffTraining = true,
                    FacetIpRelease = true,
                    CreatedBy = "seed-dm",
                    ReasonForChange = ChangeReason,
                });
            }

            await db.SaveChangesAsync(ct);
        }

        using var client = factory.CreateClient();
        var dmIdentity = new GatewayIdentity(
            UserId: "sponsor-dm-1",
            Roles: ["sponsor_dm"],
            SiteIds: [],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [StudyId],
            ProductFamilies: TestSecurityContext.AllProductFamilies,
            ChangeReason: ChangeReason);

        // 1. Single-site certify returns HTTP 200 OK using GatewayAuthenticationOptions without burning X-Sig-Token on HTTP 500.
        string certifyPath = $"/api/v1/ctms/startup/sites/{SiteId}/greenlight/certify";
        string sigToken = TestSecurityContext.CreateSigToken(
            userId: dmIdentity.UserId,
            action: certifyPath,
            semanticAction: "ctms.greenlight.certify",
            roles: dmIdentity.Roles,
            secret: OptionsSecretMarker);

        using var certifyReq = CreateSignedRequest(
            HttpMethod.Post,
            $"{certifyPath}?study_id={StudyId}",
            dmIdentity with { SigToken = sigToken },
            OptionsSecretMarker);
        using var certifyResp = await client.SendAsync(certifyReq, ct);

        certifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var certifiedGate = await certifyResp.Content.ReadFromJsonAsync<SiteGreenlightGateResponseDto>(cancellationToken: ct);
        certifiedGate.Should().NotBeNull();
        certifiedGate!.GreenlightToken.Should().NotBeNullOrWhiteSpace();
        VerifyGreenlightTokenHmac(certifiedGate.GreenlightToken!, OptionsSecretMarker).Should().BeTrue();
        VerifyGreenlightTokenHmac(certifiedGate.GreenlightToken!, RawDivergentMarker).Should().BeFalse();

        // 2. Batch certify returns HTTP 200 OK and seals each token with GatewayAuthenticationOptions.
        const string batchPath = "/api/v1/ctms/startup/matrix/batch-certify";
        string batchSigToken = TestSecurityContext.CreateSigToken(
            userId: dmIdentity.UserId,
            action: batchPath,
            semanticAction: "ctms.greenlight.batch_certify",
            roles: dmIdentity.Roles,
            secret: OptionsSecretMarker);

        using var batchReq = CreateSignedRequest(
            HttpMethod.Post,
            batchPath,
            dmIdentity with { SigToken = batchSigToken },
            OptionsSecretMarker,
            JsonContent.Create(new BatchCertifyRequestDto(
                StudyId: StudyId,
                CountryCode: "US",
                SiteIds: ["SITE-02"],
                Meaning: ChangeReason)));
        using var batchResp = await client.SendAsync(batchReq, ct);

        batchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchBody = await batchResp.Content.ReadFromJsonAsync<BatchCertifyResponseDto>(cancellationToken: ct);
        batchBody.Should().NotBeNull();
        batchBody!.CertifiedSites.Should().ContainSingle();
        VerifyGreenlightTokenHmac(batchBody.CertifiedSites[0].GreenlightToken, OptionsSecretMarker).Should().BeTrue();
        VerifyGreenlightTokenHmac(batchBody.CertifiedSites[0].GreenlightToken, RawDivergentMarker).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task EtmfStageQc_WithDivergentRawConfig_SignsOutboundWebhookWithValidatedOptions()
    {
        var ct = TestContext.Current.CancellationToken;
        string dbName = $"etmf-gateway-options-{Guid.NewGuid():N}";
        var capturedWebhooks = new ConcurrentQueue<Dictionary<string, string>>();

        await using var factory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestPostgresConnectionString()
                .UseTestStorage()
                .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, RawDivergentMarker);

            builder.ConfigureTestServices(services =>
            {
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);
                services.Configure<GatewayAuthenticationOptions>(o => o.GatewaySecret = OptionsSecretMarker);

                services.RemoveAll<DbContextOptions<EtmfDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<EtmfDbContext>>();
                services.AddDbContext<EtmfDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                    options.UseInternalServiceProvider(TestSupport.TestEfServiceProviders.InMemory);
                });

                services.AddHttpClient<IEtmfCompletenessWebhookPublisher, HttpEtmfCompletenessWebhookPublisher>(client =>
                {
                    client.BaseAddress = new Uri("http://execution.internal");
                }).ConfigurePrimaryHttpMessageHandler(() => new StubMessageHandler(req =>
                {
                    capturedWebhooks.Enqueue(CaptureHeaders(req));
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }));

                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                    logging.AddFilter("MassTransit", LogLevel.None);
                });
            });
        });

        using var client = factory.CreateClient();
        var crcIdentity = new GatewayIdentity(
            UserId: "crc-site-01",
            Roles: ["crc"],
            SiteIds: [SiteId],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [StudyId],
            ProductFamilies: TestSecurityContext.AllProductFamilies,
            ChangeReason: ChangeReason);

        using var stageReq = CreateSignedRequest(
            HttpMethod.Post,
            "/api/v1/etmf/intelligence/stage-qc",
            crcIdentity,
            OptionsSecretMarker,
            JsonContent.Create(new
            {
                Content = "FDA Form 1572 signed by Principal Investigator",
                Filename = "FDA_Form_1572_SITE_01.pdf",
                StudyId,
                SiteId,
                ReasonForChange = ChangeReason,
            }));
        using var stageResp = await client.SendAsync(stageReq, ct);

        stageResp.StatusCode.Should().Be(HttpStatusCode.Created);
        capturedWebhooks.Should().ContainSingle();
        capturedWebhooks.TryDequeue(out var capturedHeaders).Should().BeTrue();
        capturedHeaders![GatewayHeaders.UserId].Should().Be("svc:etmf");
        VerifyWebhookSignature(capturedHeaders, OptionsSecretMarker).Should().BeTrue();
        VerifyWebhookSignature(capturedHeaders, RawDivergentMarker).Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task PaymentsEndpoints_IgnoreProcessEnvironmentVariable_AndEnforceMinimumSecretLength()
    {
        var ct = TestContext.Current.CancellationToken;

        // Direct helper length validation checks.
        foreach (string? invalidSecret in new string?[] { null, string.Empty, RawDivergentMarker })
        {
            var invalidOptions = Options.Create(new GatewayAuthenticationOptions { GatewaySecret = invalidSecret });
            Action resolveAct = () => PaymentEndpoints.ResolveConfiguredGatewaySecret(invalidOptions);
            resolveAct.Should().Throw<InvalidOperationException>().WithMessage("*GATEWAY_SECRET*");

            var sampleOcrPayload = new PaymentHmacTokenHelper.ReceiptOcrTokenPayload(
                ScanId: "OCR_SCAN_TEST",
                CallerUserId: "subj-1",
                ReceiptSha256: "abc",
                OcrReadable: true,
                MerchantName: "METRO TRANSIT",
                ExpenseDate: "2026-08-23",
                GrossAmount: 20m,
                Category: "TRANSIT",
                ConfidenceScore: 0.95,
                IssuedAtUnix: 1_700_000_000,
                ExpiresAtUnix: 1_700_001_800);
            Action issueOcrAct = () => PaymentEndpoints.IssueReceiptOcrToken(sampleOcrPayload, invalidSecret);
            issueOcrAct.Should().Throw<InvalidOperationException>().WithMessage("*GATEWAY_SECRET*");

            Action issueDiaryAct = () => PaymentEndpoints.IssueDiarySyncVerificationToken(
                "subj-1",
                StudyId,
                "DIARY-01",
                "ASSIGN-01",
                1,
                new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
                invalidSecret);
            issueDiaryAct.Should().Throw<InvalidOperationException>().WithMessage("*GATEWAY_SECRET*");
        }

        await using var sqlite = new SqliteConnection("DataSource=:memory:");
        await sqlite.OpenAsync(ct);

        await using var factory = new WebApplicationFactory<Cadence.Apps.Payments.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestPostgresConnectionString()
                .UseTestFieldProtectionKeys()
                .UseSetting(CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey, RawDivergentMarker);

            builder.ConfigureTestServices(services =>
            {
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);
                services.Configure<GatewayAuthenticationOptions>(o => o.GatewaySecret = OptionsSecretMarker);

                services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PaymentsDbContext>>();
                services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(sqlite));
            });
        });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            var now = SystemClock.Instance.UtcNow;
            db.ParticipantStipendRules.Add(new ParticipantStipendRule
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = StudyId,
                VisitCode = "DIARY-DAILY-01",
                VisitName = "Daily ePRO Diary",
                Amount = 15.00m,
                Currency = "USD",
                Taxable = false,
                RequiresVerification = false,
                IsMilestone = false,
                AutoDisbursementThreshold = 250.00m,
                AntiCoercionCap = 200.00m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "seed",
                UpdatedAt = now,
                VersionIndex = 1,
            });
            await db.SaveChangesAsync(ct);
        }

        using var client = factory.CreateClient();
        string? previousEnvSecret;
        lock (EnvironmentLock)
        {
            previousEnvSecret = Environment.GetEnvironmentVariable(
                CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey);
            Environment.SetEnvironmentVariable(
                CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey,
                RawDivergentMarker);
        }

        try
        {
            var resolvedOptions = factory.Services.GetRequiredService<IOptions<GatewayAuthenticationOptions>>();
            PaymentEndpoints.ResolveConfiguredGatewaySecret(resolvedOptions).Should().Be(OptionsSecretMarker);

            byte[] receiptPng = ReceiptOcrEngine.RenderReceiptPng(
            [
                "METRO TRANSIT",
                "DATE 2026-08-23",
                "FARE PAID $28.50",
            ]);
            string receiptBase64 = Convert.ToBase64String(receiptPng);

            // 1. Staff ExtractStaffReceiptOcr + SubmitStaffExpenseClaim use OptionsSecretMarker, ignoring GATEWAY_SECRET env var.
            var crcIdentity = new GatewayIdentity(
                UserId: "crc-pay-01",
                Roles: ["crc"],
                SiteIds: [SiteId],
                TenantId: TestSecurityContext.TenantCode,
                StudyIds: [StudyId],
                ProductFamilies: TestSecurityContext.AllProductFamilies,
                ChangeReason: ChangeReason);

            using var staffOcrReq = CreateSignedRequest(
                HttpMethod.Post,
                "/api/v1/payments/participant/expenses/ocr-extract",
                crcIdentity,
                OptionsSecretMarker,
                JsonContent.Create(new OcrExtractRequestDto(ImageBase64: receiptBase64, Filename: "transit.png")));
            using var staffOcrResp = await client.SendAsync(staffOcrReq, ct);
            staffOcrResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var staffOcrBody = await staffOcrResp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(cancellationToken: ct);
            staffOcrBody.Should().NotBeNull();
            staffOcrBody!.OcrToken.Should().NotBeNullOrWhiteSpace();
            PaymentHmacTokenHelper.TryVerifyReceiptOcrToken(
                staffOcrBody.OcrToken,
                OptionsSecretMarker,
                crcIdentity.UserId,
                SystemClock.Instance.UtcNow,
                out _).Should().BeTrue();
            PaymentHmacTokenHelper.TryVerifyReceiptOcrToken(
                staffOcrBody.OcrToken,
                RawDivergentMarker,
                crcIdentity.UserId,
                SystemClock.Instance.UtcNow,
                out _).Should().BeFalse();

            using var staffClaimReq = CreateSignedRequest(
                HttpMethod.Post,
                "/api/v1/payments/participant/expenses",
                crcIdentity,
                OptionsSecretMarker,
                JsonContent.Create(new SubmitExpenseClaimRequestDto(
                    SubjectId: "SUBJ-PAY-STAFF-01",
                    StudyId: StudyId,
                    SiteId: SiteId,
                    Category: "TRANSIT",
                    MerchantName: "METRO TRANSIT",
                    GrossAmount: 28.50m,
                    ExpenseDate: "2026-08-23",
                    OcrToken: staffOcrBody.OcrToken)));
            using var staffClaimResp = await client.SendAsync(staffClaimReq, ct);
            staffClaimResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var staffClaim = await staffClaimResp.Content.ReadFromJsonAsync<ParticipantExpenseClaim>(cancellationToken: ct);
            staffClaim!.Status.Should().Be("APPROVED");

            // 2. Participant self-service ExtractSelfReceiptOcr, SubmitSelfExpenseClaim, and CreditSelfDiaryStipend.
            var subjectIdentity = new GatewayIdentity(
                UserId: "SUBJ-PAY-SELF-01",
                Roles: ["subject"],
                SiteIds: [SiteId],
                TenantId: TestSecurityContext.TenantCode,
                StudyIds: [StudyId],
                ProductFamilies: TestSecurityContext.AllProductFamilies,
                ChangeReason: ChangeReason);

            byte[] mealPng = ReceiptOcrEngine.RenderReceiptPng(
            [
                "BISTRO CAFE",
                "DATE 2026-10-05",
                "TOTAL $19.25",
            ]);
            using var selfOcrReq = CreateSignedRequest(
                HttpMethod.Post,
                "/api/v1/payments/participant/me/expenses/ocr-extract",
                subjectIdentity,
                OptionsSecretMarker,
                JsonContent.Create(new OcrExtractRequestDto(ImageBase64: Convert.ToBase64String(mealPng), Filename: "meal.png")));
            using var selfOcrResp = await client.SendAsync(selfOcrReq, ct);
            selfOcrResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var selfOcrBody = await selfOcrResp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(cancellationToken: ct);
            selfOcrBody!.OcrToken.Should().NotBeNullOrWhiteSpace();
            PaymentHmacTokenHelper.TryVerifyReceiptOcrToken(
                selfOcrBody.OcrToken,
                OptionsSecretMarker,
                subjectIdentity.UserId,
                SystemClock.Instance.UtcNow,
                out _).Should().BeTrue();

            using var selfClaimReq = CreateSignedRequest(
                HttpMethod.Post,
                "/api/v1/payments/participant/me/expenses",
                subjectIdentity,
                OptionsSecretMarker,
                JsonContent.Create(new SelfServiceSubmitExpenseRequestDto(
                    StudyCode: StudyId,
                    SiteCode: SiteId,
                    Category: "MEAL",
                    MerchantName: "BISTRO CAFE",
                    GrossAmount: 19.25m,
                    ExpenseDate: "2026-10-05",
                    OcrToken: selfOcrBody.OcrToken)));
            using var selfClaimResp = await client.SendAsync(selfClaimReq, ct);
            selfClaimResp.StatusCode.Should().Be(HttpStatusCode.OK);

            string diaryToken = PaymentEndpoints.IssueDiarySyncVerificationToken(
                callerUserId: subjectIdentity.UserId,
                studyCode: StudyId,
                diaryId: "DIARY-DAILY-01",
                assignmentId: "ASSIGN-01",
                sequenceNumber: 1,
                issuedAt: SystemClock.Instance.UtcNow,
                gatewaySecret: OptionsSecretMarker);

            using var diaryReq = CreateSignedRequest(
                HttpMethod.Post,
                "/api/v1/payments/participant/me/diary-stipend",
                subjectIdentity,
                OptionsSecretMarker,
                JsonContent.Create(new SelfServiceDiaryStipendRequestDto
                {
                    StudyCode = StudyId,
                    SiteCode = SiteId,
                    DiaryId = "DIARY-DAILY-01",
                    AssignmentId = "ASSIGN-01",
                    SequenceNumber = 1,
                    SyncVerificationToken = diaryToken,
                }));
            using var diaryResp = await client.SendAsync(diaryReq, ct);
            diaryResp.StatusCode.Should().Be(HttpStatusCode.OK);
            string diaryJson = await diaryResp.Content.ReadAsStringAsync(ct);
            diaryJson.Should().Contain("CREDITED");
        }
        finally
        {
            lock (EnvironmentLock)
            {
                Environment.SetEnvironmentVariable(
                    CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey,
                    previousEnvSecret);
            }
        }
    }

    private static HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string pathAndQuery,
        GatewayIdentity identity,
        string gatewaySecret,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, pathAndQuery)
        {
            Content = content,
        };
        foreach (var (name, value) in TestSecurityContext.CreateHmacHeaders(identity, secret: gatewaySecret))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static Dictionary<string, string> CaptureHeaders(HttpRequestMessage request) =>
        request.Headers.ToDictionary(
            static h => h.Key,
            static h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);

    private static bool VerifyGreenlightTokenHmac(string token, string secret)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        string expectedHex = Convert.ToHexStringLower(expectedBytes);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(expectedHex));
    }

    private static bool VerifyWebhookSignature(IReadOnlyDictionary<string, string> headers, string secret)
    {
        string Get(string key) => headers.TryGetValue(key, out string? value) ? value : string.Empty;
        static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        string tenantReadOnlyRaw = Get(GatewayHeaders.TenantReadOnly);
        bool tenantReadOnly = string.Equals(tenantReadOnlyRaw, "true", StringComparison.OrdinalIgnoreCase);
        bool unblindedAccess = string.Equals(Get(GatewayHeaders.UnblindedAccess), "true", StringComparison.OrdinalIgnoreCase);
        ChangeReasonHeader.TryDecode(Get(GatewayHeaders.ChangeReason), out string? decodedReason);

        return HmacSigner.VerifyGatewaySignature(
            userId: Get(GatewayHeaders.UserId),
            roles: Get(GatewayHeaders.Roles),
            timestamp: Get(GatewayHeaders.Timestamp),
            signature: Get(GatewayHeaders.Signature),
            secret: GatewayIdentity.EncodeSecret(secret),
            changeReason: NullIfBlank(decodedReason),
            siteId: NullIfBlank(Get(GatewayHeaders.SiteId)),
            studyIds: NullIfBlank(Get(GatewayHeaders.StudyIds)),
            sponsorId: NullIfBlank(Get(GatewayHeaders.SponsorId)),
            unblindedAccess: unblindedAccess,
            tenantId: NullIfBlank(Get(GatewayHeaders.TenantId)),
            sigToken: NullIfBlank(Get(GatewayHeaders.SigToken)),
            tenantReadOnly: tenantReadOnly,
            tenantProductFamilies: NullIfBlank(Get(GatewayHeaders.TenantProductFamilies)),
            tenantScoping: NullIfBlank(Get(GatewayHeaders.TenantScoping)));
    }

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
