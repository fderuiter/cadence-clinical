using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.ToolingCore.OpenApi;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// The local Keycloak realm puts each user's tenant in the token the gateway reads it from (#5038). The gateway has no
/// fallback tenant, so a realm user without one could reach no kernel tenant at all.
/// </summary>
public sealed class KeycloakRealmTenantTests
{
    private static JsonElement Realm()
    {
        string path = Path.Combine(OpenApiToolingService.FindRepoRoot(), "docker", "cadence-realm.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [Fact]
    public void WebClient_MapsTheTenantAttributeIntoTheAccessToken()
    {
        var web = Realm().GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("clientId").GetString() == "cadence-web");

        var tenant = web.GetProperty("protocolMappers").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "tenant").GetProperty("config");

        tenant.GetProperty("user.attribute").GetString().Should().Be("tenant_id");
        tenant.GetProperty("claim.name").GetString().Should().Be("tenant_id");
        tenant.GetProperty("access.token.claim").GetString().Should().Be("true");
    }

    /// <summary>The web client carries the caller's study assignments, which study-scoped routes need (ADR-2516).</summary>
    [Fact]
    public void WebClient_MapsTheStudyAssignmentsIntoTheAccessToken()
    {
        var web = Realm().GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("clientId").GetString() == "cadence-web");

        var studies = web.GetProperty("protocolMappers").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "study assignments").GetProperty("config");

        studies.GetProperty("user.attribute").GetString().Should().Be("study_ids");
        studies.GetProperty("claim.name").GetString().Should().Be("study_ids");
        studies.GetProperty("multivalued").GetString().Should().Be("true");
        studies.GetProperty("access.token.claim").GetString().Should().Be("true");
    }

    /// <summary>
    /// Every realm user has a fixed, unique subject id, so the kernel host's demo seed can grant them by the subject the
    /// gateway forwards (ADR-2543, #5452); without one, Keycloak generates a new id on every import.
    /// </summary>
    [Fact]
    public void EveryRealmUser_HasAFixedSubjectId()
    {
        var ids = Realm().GetProperty("users").EnumerateArray()
            .Select(u => u.TryGetProperty("id", out var id) ? id.GetString() : null)
            .ToList();

        ids.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        ids.Should().AllSatisfy(id => System.Guid.TryParse(id, out var _).Should().BeTrue($"'{id}' is a subject id"));
    }

    [Fact]
    public void EveryRealmUser_HasATenant()
    {
        var users = Realm().GetProperty("users").EnumerateArray().ToList();

        users.Should().NotBeEmpty();
        users.Where(u => TenantOf(u) is null).Select(u => u.GetProperty("username").GetString()).Should().BeEmpty();
    }

    [Fact]
    public void WebClient_MapsUnblindedAccessAttributeIntoTheAccessToken()
    {
        var web = Realm().GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("clientId").GetString() == "cadence-web");

        var unblinded = web.GetProperty("protocolMappers").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "unblinded access").GetProperty("config");

        unblinded.GetProperty("user.attribute").GetString().Should().Be("unblinded_access");
        unblinded.GetProperty("claim.name").GetString().Should().Be("unblinded_access");
        unblinded.GetProperty("access.token.claim").GetString().Should().Be("true");
    }

    [Fact]
    public void Realm_DeclaresRtsmAndEmergencyUnblindingRoles_AndSeedsTwoStatisticiansPharmacistAndPi()
    {
        var root = Realm();
        var roleNames = root.GetProperty("roles").GetProperty("realm").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToList();

        roleNames.Should().Contain([
            "unblinded_statistician",
            "pharmacist",
            "Principal Investigator",
            "Authorized ER Physician",
        ]);

        var users = root.GetProperty("users").EnumerateArray().ToList();
        var pi = users.Single(u => u.GetProperty("username").GetString() == "pi.site101");
        pi.GetProperty("realmRoles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain("Principal Investigator");

        var statisticians = users.Where(u =>
            u.GetProperty("realmRoles").EnumerateArray().Any(r => r.GetString() == "unblinded_statistician")).ToList();
        statisticians.Should().HaveCountGreaterThanOrEqualTo(2);
        statisticians.Should().AllSatisfy(u =>
            u.GetProperty("attributes").GetProperty("unblinded_access")[0].GetString().Should().Be("true"));

        var pharmacists = users.Where(u =>
            u.GetProperty("realmRoles").EnumerateArray().Any(r => r.GetString() == "pharmacist")).ToList();
        pharmacists.Should().ContainSingle();
        pharmacists[0].GetProperty("attributes").GetProperty("unblinded_access")[0].GetString().Should().Be("true");
    }

    private static string? TenantOf(JsonElement user) =>
        user.TryGetProperty("attributes", out var attributes)
        && attributes.TryGetProperty("tenant_id", out var tenant)
        && tenant.GetArrayLength() == 1
        && tenant[0].GetString() is { } code
        && !string.IsNullOrWhiteSpace(code)
            ? code
            : null;
}
