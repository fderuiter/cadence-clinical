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

    [Fact]
    public void WebClient_MapsTheSiteAssignmentsIntoTheAccessToken()
    {
        var web = Realm().GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("clientId").GetString() == "cadence-web");

        var sites = web.GetProperty("protocolMappers").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "site assignments").GetProperty("config");

        sites.GetProperty("user.attribute").GetString().Should().Be("site_id");
        sites.GetProperty("claim.name").GetString().Should().Be("site_id");
        sites.GetProperty("multivalued").GetString().Should().Be("true");
        sites.GetProperty("access.token.claim").GetString().Should().Be("true");
    }

    [Fact]
    public void Realm_SeedsDeterministicSubjectAccountWithScopedAttributes()
    {
        var subject = Realm().GetProperty("users").EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == "subject.site101");

        subject.GetProperty("enabled").GetBoolean().Should().BeTrue();
        System.Guid.TryParse(subject.GetProperty("id").GetString(), out _).Should().BeTrue();

        var roles = subject.GetProperty("realmRoles").EnumerateArray().Select(r => r.GetString()).ToList();
        roles.Should().Equal("Subject");

        var credentials = subject.GetProperty("credentials").EnumerateArray().ToList();
        credentials.Should().ContainSingle(c =>
            c.GetProperty("type").GetString() == "password"
            && !string.IsNullOrEmpty(c.GetProperty("value").GetString())
            && !c.GetProperty("temporary").GetBoolean());

        var attributes = subject.GetProperty("attributes");
        AttributeValues(attributes, "tenant_id").Should().Equal("cadence-local");
        AttributeValues(attributes, "study_ids").Should().Equal("CADENCE-101");
        AttributeValues(attributes, "site_id").Should().Equal("SITE-101");
    }

    [Fact]
    public void Realm_UserProfileLocksTenantStudyAndSiteAttributesToAdminEditOnly()
    {
        var provider = Realm()
            .GetProperty("components")
            .GetProperty("org.keycloak.userprofile.UserProfileProvider")
            .EnumerateArray()
            .Single();

        string rawConfig = provider
            .GetProperty("config")
            .GetProperty("kc.user.profile.config")[0]
            .GetString()!;

        using var profileDoc = JsonDocument.Parse(rawConfig);
        var root = profileDoc.RootElement;

        if (root.TryGetProperty("unmanagedAttributePolicy", out var unmanaged))
        {
            unmanaged.GetString().Should().NotBe("ENABLED");
        }

        var attrsByName = root.GetProperty("attributes")
            .EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a);

        foreach (string protectedAttr in new[] { "tenant_id", "study_ids", "site_id" })
        {
            attrsByName.Should().ContainKey(protectedAttr);
            var editRoles = attrsByName[protectedAttr]
                .GetProperty("permissions")
                .GetProperty("edit")
                .EnumerateArray()
                .Select(e => e.GetString())
                .ToList();
            editRoles.Should().Equal("admin");
            editRoles.Should().NotContain("user");
        }
    }

    private static string[] AttributeValues(JsonElement attributes, string name) =>
        [.. attributes.GetProperty(name).EnumerateArray().Select(v => v.GetString()!)];

    private static string? TenantOf(JsonElement user) =>
        user.TryGetProperty("attributes", out var attributes)
        && attributes.TryGetProperty("tenant_id", out var tenant)
        && tenant.GetArrayLength() == 1
        && tenant[0].GetString() is { } code
        && !string.IsNullOrWhiteSpace(code)
            ? code
            : null;
}
