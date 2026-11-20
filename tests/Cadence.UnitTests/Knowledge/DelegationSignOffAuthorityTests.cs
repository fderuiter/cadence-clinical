using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

/// <summary>
/// The PI's bulk delegation-log sign-off through the real knowledge host (#5311). It needs the
/// <c>delegation_log:sign</c> permission and a fresh step-up token bound to the delegation ids, records the verified
/// caller as the signer, keys its seal, and never creates the delegation records it is asked to sign. Before the
/// fix any signed-in caller with a non-empty password string in the body could sign, the signer and role came from
/// the body, and unknown ids were invented as completed, PI-signed records.
/// </summary>
public sealed class DelegationSignOffAuthorityTests(DelegationSignOffAuthorityTests.KnowledgeFactory factory)
    : IClassFixture<DelegationSignOffAuthorityTests.KnowledgeFactory>
{
    private const string Path = "/api/v1/training/delegations/bulk-signoff";
    private const string SemanticAction = "knowledge.delegation.sign_off";

    private static readonly GatewayIdentity Pi = TestSecurityContext.Identity("pi-knowledge", "Principal Investigator") with
    {
        SiteIds = ["SITE-101"],
    };

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BulkSignOff_WithoutSigToken_Returns401AndSignsNothing()
    {
        string id = await SeedPendingAsync();
        using var client = factory.CreateSignedClient(Pi);

        var response = await client.PostAsJsonAsync(Path, Body(id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(Ct));
        (await RecordAsync(id))!.SignedOff.Should().BeFalse();
    }

    [Theory]
    [GxPRequirement("PRD-TRN-003")]
    [InlineData("Clinical Research Coordinator")]
    [InlineData("CRA")]
    [InlineData("Auditor")]
    public async Task BulkSignOff_WithoutDelegationSignPermission_Returns403(string role)
    {
        string id = await SeedPendingAsync();
        var caller = TestSecurityContext.Identity("not-a-pi", role);
        using var client = factory.CreateSignedClient(caller);

        var response = await SendAsync(client, TokenFor(caller.UserId, id), Body(id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await RecordAsync(id))!.SignedOff.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-TRN-003")]
    public async Task BulkSignOff_WithAnUnknownDelegationId_Returns404AndCreatesNothing()
    {
        string known = await SeedPendingAsync();
        string unknown = $"del-unknown-{Guid.NewGuid():N}";
        using var client = factory.CreateSignedClient(Pi);

        var response = await SendAsync(client, TokenFor(Pi.UserId, known, unknown), Body(known, unknown));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(Ct));
        (await RecordAsync(unknown)).Should().BeNull();
        (await RecordAsync(known))!.SignedOff.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BulkSignOff_WithATokenForOtherDelegations_Returns401()
    {
        string id = await SeedPendingAsync();
        string other = await SeedPendingAsync();
        using var client = factory.CreateSignedClient(Pi);

        var response = await SendAsync(client, TokenFor(Pi.UserId, other), Body(id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RecordAsync(id))!.SignedOff.Should().BeFalse();
    }

    /// <summary>The verified caller is the signer and audit actor whatever the body says, and the seal is keyed.</summary>
    [Fact]
    [GxPRequirement("PRD-TRN-003")]
    public async Task BulkSignOff_WithTokenForTheDelegations_SignsAsTheVerifiedCaller()
    {
        string id = await SeedPendingAsync();
        using var client = factory.CreateSignedClient(Pi);

        var response = await SendAsync(client, TokenFor(Pi.UserId, id), Body(id));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var record = (await RecordAsync(id))!;
        record.SignedOff.Should().BeTrue();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var audit = await db.OrgAuditLogs.AsNoTracking().SingleAsync(a => a.BatchTransactionId == record.BatchTransactionId, Ct);
        audit.ActorId.Should().Be(Pi.UserId);
        audit.ActorRole.Should().NotBe("Someone Else's Role");
        var certificate = await db.TrainingCertificates.AsNoTracking().SingleAsync(c => c.BatchTransactionId == record.BatchTransactionId, Ct);
        certificate.SignerName.Should().Be(Pi.UserId);
        string unkeyed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record.BatchTransactionId!)));
        record.PiSignatureHash.Should().NotBeNullOrEmpty().And.NotBe(unkeyed);
    }

    private static object Body(params string[] ids) => new
    {
        delegation_ids = ids,
        study_id = "CARDIO-2026",
        site_id = "SITE-101",
        signature_payload = new
        {
            signer_name = "Someone Else",
            signer_role = "Someone Else's Role",
            meaning = "I approve these delegations",
            password = "not-checked-by-this-service",
            reason_for_change = "PI delegation sign-off",
        },
    };

    private static string TokenFor(string userId, params string[] ids) =>
        TestSecurityContext.CreateSigToken(userId, Path, SemanticAction, batchId: BatchId(ids));

    /// <summary>The lower-case hex SHA-256 of the trimmed ids, sorted ordinally and comma-joined.</summary>
    private static string BatchId(params string[] ids) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids.Order(StringComparer.Ordinal)))));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayHeaders.SigToken, token);
        return await client.SendAsync(request, Ct);
    }

    private async Task<string> SeedPendingAsync()
    {
        string id = $"del-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.DelegationRecords.Add(new DelegationRecord
        {
            Id = id,
            StudyId = "CARDIO-2026",
            SiteId = "SITE-101",
            StaffUserId = "staff-1",
            StaffName = "Site staff",
            Role = "Clinical Research Coordinator",
            TaskCode = "EDC_DATA_ENTRY",
            Duties = "eCRF data entry",
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "pi-knowledge",
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<DelegationRecord?> RecordAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        return await db.DelegationRecords.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, Ct);
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The knowledge host on an isolated EF Core in-memory database.</summary>
    public sealed class KnowledgeFactory : WebApplicationFactory<Cadence.Apps.Knowledge.Program>
    {
        private readonly string _databaseName = $"knowledge-signoff-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                var inMemory = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
                services.RemoveAll<DbContextOptions<KnowledgeDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KnowledgeDbContext>>();
                services.AddDbContext<KnowledgeDbContext>((sp, options) => options
                    .UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(inMemory)
                    .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                    .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance));

                // The durable token-use store needs PostgreSQL; this in-memory host keeps uses in process memory.
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(new ReplayCache());
            });
        }
    }
}
