using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Security.Rbac;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>The role rules behind the subject routes (#4943) and self-service trial coordinator scoping.</summary>
public sealed class SubjectAuthorityTests
{
    [Theory]
    [InlineData("investigator", true)]
    [InlineData("principal_investigator", true)]
    [InlineData("sub_investigator", true)]
    [InlineData("lead_investigator", true)]
    [InlineData("crc", true)]
    [InlineData("trial_coordinator", true)]
    [InlineData("Trial Coordinator", true)]
    [InlineData("trial-coordinator", true)]
    [InlineData("sponsor_dm", false)]
    [InlineData("Sponsor Admin", false)]
    [InlineData("data_manager", false)]
    [InlineData("Data Manager", false)]
    [InlineData("dm", false)]
    [InlineData("cra", false)]
    [InlineData("monitor", false)]
    [InlineData("auditor", false)]
    [InlineData("subject", false)]
    [InlineData("emergency_unblinder", false)]
    [InlineData("", false)]
    public void CanManageParticipants_AllowsSiteAndTrialCoordinatorRolesOnly(string role, bool expected) =>
        SubjectAuthority.CanManageParticipants(role).Should().Be(expected);

    [Theory]
    [InlineData("principal_investigator", true)]
    [InlineData("Principal Investigator", true)]
    [InlineData("pi", true)]
    [InlineData("authorized_er_physician", true)]
    [InlineData("Authorized ER Physician", true)]
    [InlineData("emergency_unblinder", false)]
    [InlineData("lead_investigator", false)]
    [InlineData("investigator", false)]
    [InlineData("crc", false)]
    [InlineData("trial_coordinator", false)]
    [InlineData("Trial Coordinator", false)]
    [InlineData("sponsor_dm", false)]
    [InlineData("pharmacist", false)]
    [InlineData("", false)]
    public void CanUnblind_AllowsTheUnblindingAuthoritiesOnly(string role, bool expected) =>
        SubjectAuthority.CanUnblind(role).Should().Be(expected);

    [Fact]
    public void AuthorizeStateChange_ToUnblinded_NeedsAnUnblindingAuthority_EvenForASiteOrTrialCoordinatorRole()
    {
        var coordinator = new AuthenticatedCaller("crc-1", ["crc"]);
        var trialCoordinator = new AuthenticatedCaller("founder-1", ["Trial Coordinator"], TenantId: "trial-a1b2c3d4e5f6");
        var unblinder = new AuthenticatedCaller("er-1", ["authorized_er_physician"]);

        SubjectAuthority.AuthorizeStateChange(coordinator, "UNBLINDED")!.Code.Should().Be("UNAUTHORIZED_UNBLIND_ROLE");
        SubjectAuthority.AuthorizeStateChange(trialCoordinator, "UNBLINDED")!.Code.Should().Be("UNAUTHORIZED_UNBLIND_ROLE");
        SubjectAuthority.AuthorizeStateChange(unblinder, "UNBLINDED").Should().BeNull();
        SubjectAuthority.AuthorizeStateChange(coordinator, "WITHDRAWN").Should().BeNull();
        SubjectAuthority.AuthorizeStateChange(trialCoordinator, "WITHDRAWN").Should().BeNull();
        SubjectAuthority.AuthorizeStateChange(unblinder, "WITHDRAWN")!.Code.Should().Be("UNAUTHORIZED_SUBJECT_ROLE");
    }

    [Fact]
    public void AuthorizeParticipantChange_RestrictsTrialCoordinatorToVerifiedTrialTenants_AndPreservesSponsorDmSeparation()
    {
        var trialFounder = new AuthenticatedCaller(
            "founder-1",
            ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"],
            TenantId: "trial-a1b2c3d4e5f6");
        SubjectAuthority.AuthorizeParticipantChange(trialFounder).Should().BeNull();

        foreach (string? nonTrialTenant in new[] { null, "", "cadence-local", "01960000-0000-7000-8000-000000000001", "trial-short", "trial-ABCDEF123456", "trial-a1b2c3d4e5f6-extra" })
        {
            var nonTrialCoordinator = new AuthenticatedCaller(
                "user-non-trial",
                ["Trial Coordinator"],
                TenantId: nonTrialTenant);
            SubjectAuthority.AuthorizeParticipantChange(nonTrialCoordinator)!.Code
                .Should().Be("UNAUTHORIZED_SUBJECT_ROLE", $"tenant '{nonTrialTenant}' is not a verified trial-<12hex> tenant");
        }

        // Separation of duties: Sponsor Admin / sponsor_dm without Trial Coordinator remains refused even in a trial tenant.
        var sponsorOnlyInTrial = new AuthenticatedCaller(
            "sponsor-only",
            ["Sponsor Admin", "Sponsor Designer"],
            TenantId: "trial-a1b2c3d4e5f6");
        SubjectAuthority.AuthorizeParticipantChange(sponsorOnlyInTrial)!.Code.Should().Be("UNAUTHORIZED_SUBJECT_ROLE");
    }

    [Fact]
    public void TrialCoordinatorRole_NormalizesCleanly_GrantsSubjectAndEcrfPermissions_RemainsUnscopedBySite_AndExistsInKeycloakRealm()
    {
        RoleNormalization.NormalizeRole("Trial Coordinator").Should().Be(RoleNormalization.ROLE_TRIAL_COORDINATOR);
        RoleNormalization.NormalizeRole("trial-coordinator").Should().Be(RoleNormalization.ROLE_TRIAL_COORDINATOR);
        RoleNormalization.NormalizeRole("trial_coordinator").Should().Be(RoleNormalization.ROLE_TRIAL_COORDINATOR);

        SiteAccessEvaluator.SITE_SCOPED_ROLES.Should().NotContain(RoleNormalization.ROLE_TRIAL_COORDINATOR);

        var trialPrincipal = new Principal { UserId = "founder-01", RawRoles = ["Trial Coordinator"] };
        PermissionMatrix.HasPermission(trialPrincipal, "subject_enrollment:create").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "subject_enrollment:update").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "subject_enrollment:randomize").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "ecrf_data_entry:create").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "ecrf_data_entry:update").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "cgt_chain_of_custody:read").Should().BeTrue();
        PermissionMatrix.HasPermission(trialPrincipal, "cgt_chain_of_custody:write").Should().BeFalse();

        // Unblinded statistician reads subject enrollment for allocation reconciliation without subject write or CGT custody read grants.
        var statPrincipal = new Principal { UserId = "stat-01", RawRoles = ["unblinded_statistician"] };
        PermissionMatrix.HasPermission(statPrincipal, "subject_enrollment:read").Should().BeTrue();
        PermissionMatrix.HasPermission(statPrincipal, "subject_enrollment:create").Should().BeFalse();
        PermissionMatrix.HasPermission(statPrincipal, "subject_enrollment:update").Should().BeFalse();
        PermissionMatrix.HasPermission(statPrincipal, "subject_enrollment:randomize").Should().BeFalse();
        PermissionMatrix.HasPermission(statPrincipal, "cgt_chain_of_custody:read").Should().BeFalse();
        PermissionMatrix.HasPermission(statPrincipal, "cgt_chain_of_custody:write").Should().BeFalse();

        // Separation of duties: sponsor_dm still cannot create or update subjects or eCRF entries.
        var sponsorPrincipal = new Principal { UserId = "sponsor-01", RawRoles = ["Sponsor Admin"] };
        PermissionMatrix.HasPermission(sponsorPrincipal, "subject_enrollment:create").Should().BeFalse();
        PermissionMatrix.HasPermission(sponsorPrincipal, "subject_enrollment:update").Should().BeFalse();
        PermissionMatrix.HasPermission(sponsorPrincipal, "ecrf_data_entry:create").Should().BeFalse();

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string realmJson = File.ReadAllText(Path.Combine(repoRoot, "docker", "cadence-realm.json"));
        using var doc = JsonDocument.Parse(realmJson);
        var realmRoles = doc.RootElement
            .GetProperty("roles")
            .GetProperty("realm")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToList();
        realmRoles.Should().Contain("Trial Coordinator");
    }

    [Fact]
    public void AuthorizeParticipantChange_RefusesAnUnauthenticatedCaller() =>
        SubjectAuthority.AuthorizeParticipantChange(null)!.Code.Should().Be("UNAUTHORIZED_SUBJECT_ROLE");
}
