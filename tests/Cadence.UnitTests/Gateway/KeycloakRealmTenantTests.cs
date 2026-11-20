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
    public void Realm_AllowsSelfRegistrationAndEmailSignIn()
    {
        var realm = Realm();

        realm.GetProperty("registrationAllowed").GetBoolean().Should().BeTrue();
        realm.GetProperty("loginWithEmailAllowed").GetBoolean().Should().BeTrue();
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

    private static string? TenantOf(JsonElement user) =>
        user.TryGetProperty("attributes", out var attributes)
        && attributes.TryGetProperty("tenant_id", out var tenant)
        && tenant.GetArrayLength() == 1
        && tenant[0].GetString() is { } code
        && !string.IsNullOrWhiteSpace(code)
            ? code
            : null;
}
