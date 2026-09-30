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
            .Single(m => m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-attribute-mapper").GetProperty("config");

        tenant.GetProperty("user.attribute").GetString().Should().Be("tenant_id");
        tenant.GetProperty("claim.name").GetString().Should().Be("tenant_id");
        tenant.GetProperty("access.token.claim").GetString().Should().Be("true");
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
