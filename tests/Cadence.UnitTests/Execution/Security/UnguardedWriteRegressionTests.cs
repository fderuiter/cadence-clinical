using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Application.Supply;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Writes that any signed-in caller could make before routes were denied by default (#5361, ADR-2516): seeding a site's
/// IP kit inventory, dispensing, quarantining and releasing an investigational device, and committing a bulk lab-range
/// import. A signed caller whose roles lack the route permission is refused with
/// <c>403</c> by the authorization middleware, and nothing is written: the target table and the audit trail are
/// unchanged.
/// </summary>
public sealed class UnguardedWriteRegressionTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string Reason = "Regression check for unguarded writes";

    /// <summary>The pharmacist reconciles kits but does not seed a site's inventory; neither does a CRA.</summary>
    [Theory]
    [InlineData("Pharmacist")]
    [InlineData("CRA")]
    public async Task SeedKitInventory_ByACallerWithoutTheSponsorGrant_Returns403_AndAddsNoKits(string role)
    {
        string site = $"SITE-{Guid.NewGuid():N}";

        using var response = await SeedKitsAsync(Client(role), site);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountAsync(db => db.IpKitInventoryItems.CountAsync(k => k.SiteId == site, Ct))).Should().Be(0);
    }

    /// <summary>The same request from a data manager is admitted and writes the kit, so only the caller's role differs.</summary>
    [Fact]
    public async Task SeedKitInventory_ByADataManager_AddsTheKit()
    {
        string site = $"SITE-{Guid.NewGuid():N}";

        using var response = await SeedKitsAsync(Client("Data Manager"), site);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await CountAsync(db => db.IpKitInventoryItems.CountAsync(k => k.SiteId == site, Ct))).Should().Be(1);
    }

    /// <summary>Monitors and data managers do not hand devices to participants.</summary>
    [Theory]
    [InlineData("CRA")]
    [InlineData("Data Manager")]
    public async Task DeviceDispense_ByACallerWhoDoesNotDispense_Returns403_AndChangesNoDevice(string role)
    {
        await AssertRefusedWithoutWritesAsync(
            role,
            "/api/v1/execution/devices/dispense",
            new DeviceDispensationRequest("STUDY-REG", "SITE-REG", "SUBJ-REG", SerialNumber: "SN-REG-001"));
    }

    /// <summary>Site staff and monitors report a deficiency, which quarantines automatically; they do not quarantine directly.</summary>
    [Theory]
    [InlineData("Site Investigator")]
    [InlineData("CRA")]
    public async Task DeviceQuarantine_ByACallerWithoutTheQuarantineGrant_Returns403_AndOpensNoQuarantine(string role)
    {
        await AssertRefusedWithoutWritesAsync(
            role,
            "/api/v1/execution/devices/quarantine",
            new DeviceQuarantineCreateRequest("STUDY-REG", "SITE-REG", "Suspected malfunction", SerialNumber: "SN-REG-001"));
    }

    /// <summary>The pharmacist may quarantine a device but releasing one is a sponsor safety decision.</summary>
    [Theory]
    [InlineData("Pharmacist")]
    [InlineData("Site Investigator")]
    public async Task DeviceQuarantineRelease_ByACallerWithoutTheReleaseGrant_Returns403_AndReleasesNothing(string role)
    {
        await AssertRefusedWithoutWritesAsync(
            role,
            "/api/v1/execution/devices/quarantine/release",
            new DeviceQuarantineReleaseRequest(Guid.NewGuid().ToString(), "Root cause closed", "sig-token"));
    }

    /// <summary>A CRA may maintain a single range but not replace a study's reference data with a bulk import.</summary>
    [Theory]
    [InlineData("CRA")]
    [InlineData("Site Investigator")]
    public async Task LabRangeImportCommit_ByACallerWithoutTheImportGrant_Returns403_AndImportsNothing(string role)
    {
        int rangesBefore = await CountAsync(db => db.LabReferenceRanges.CountAsync(Ct));
        int jobsBefore = await CountAsync(db => db.BulkLabRangeImportJobs.CountAsync(Ct));
        int auditBefore = await CountAsync(db => db.AuditLogEntries.CountAsync(Ct));

        using var client = Client(role);
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "test_code,unit,lower_limit,upper_limit,sex,age_min,age_max\nALT,U/L,7,56,ALL,18,99\n"));
        form.Add(file, "file", "ranges.csv");
        form.Add(new StringContent("STUDY-REG"), "study_id");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/labs/import/commit") { Content = form };
        request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountAsync(db => db.LabReferenceRanges.CountAsync(Ct))).Should().Be(rangesBefore);
        (await CountAsync(db => db.BulkLabRangeImportJobs.CountAsync(Ct))).Should().Be(jobsBefore);
        (await CountAsync(db => db.AuditLogEntries.CountAsync(Ct))).Should().Be(auditBefore);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string role) =>
        factory.CreateSignedClient(TestSecurityContext.Identity($"regression-{role.Replace(' ', '-')}", role));

    private async Task AssertRefusedWithoutWritesAsync<TBody>(string role, string path, TBody body)
    {
        int devicesBefore = await CountAsync(db => db.DeviceInventoryItems.CountAsync(Ct));
        int quarantinesBefore = await CountAsync(db => db.DeviceQuarantineRecords.CountAsync(Ct));
        int auditBefore = await CountAsync(db => db.AuditLogEntries.CountAsync(Ct));

        using var client = Client(role);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: ExecutionApiFactory.JsonOptions),
        };
        request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountAsync(db => db.DeviceInventoryItems.CountAsync(Ct))).Should().Be(devicesBefore);
        (await CountAsync(db => db.DeviceQuarantineRecords.CountAsync(Ct))).Should().Be(quarantinesBefore);
        (await CountAsync(db => db.AuditLogEntries.CountAsync(Ct))).Should().Be(auditBefore);
    }

    private static async Task<HttpResponseMessage> SeedKitsAsync(HttpClient client, string site)
    {
        using (client)
        {
            var kits = new List<IpKitSeedItem>
            {
                new($"KIT-{site}", "LOT-REG", FakeClock.DefaultStart.AddYears(1), "AVAILABLE"),
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/rtsm/kits/seed-inventory")
            {
                Content = JsonContent.Create(new SeedInventoryRequest("STUDY-REG", site, kits), options: ExecutionApiFactory.JsonOptions),
            };
            request.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            return await client.SendAsync(request, Ct);
        }
    }

    private Task<int> CountAsync(Func<ExecutionDbContext, Task<int>> count) => ReadAsync(count);

    private async Task<T> ReadAsync<T>(Func<ExecutionDbContext, Task<T>> read)
    {
        using var client = factory.CreateSignedClient(); // builds the host on first use
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ExecutionDbContext>());
    }
}
