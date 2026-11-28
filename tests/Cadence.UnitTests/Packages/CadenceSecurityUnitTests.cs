using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Cadence.Apps.Gateway;
using Cadence.Core.Domain.Randomization;
using Cadence.Kernel.Time;
using Cadence.Security.Audit;
using Cadence.Security.Authentication;
using Cadence.Security.Blinding;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CadenceSecurityUnitTests
{
    private const string TestSecret = "test-secret-key-32-chars-long-1234567890"; // pragma: allowlist secret

    [Fact]
    public void RoleNormalization_MapsAliasesToCanonicalNames()
    {
        RoleNormalization.NormalizeRole("site investigator").Should().Be("investigator");
        RoleNormalization.NormalizeRole("Site_Investigator").Should().Be("investigator");
        RoleNormalization.NormalizeRole("PI").Should().Be("principal_investigator");
        RoleNormalization.NormalizeRole("data manager").Should().Be("sponsor_dm");
        RoleNormalization.NormalizeRole("Sponsor Admin").Should().Be("sponsor_dm");
        RoleNormalization.NormalizeRole("cro monitor").Should().Be("external_monitor");
        RoleNormalization.NormalizeRole("patient").Should().Be("subject");
        RoleNormalization.NormalizeRole("inspector").Should().Be("auditor");
        RoleNormalization.NormalizeRole("regulatory_inspector").Should().Be("auditor");
        RoleNormalization.NormalizeRole("safety_reviewer").Should().Be("sponsor_mm");
        RoleNormalization.NormalizeRole("Safety Reviewer").Should().Be("sponsor_mm");
        RoleNormalization.NormalizeRole("safety_officer").Should().Be("sponsor_mm");

        var rawRoles = new[] { "site investigator", "CRO Monitor", "PATIENT", "Safety Reviewer" };
        var normalized = RoleNormalization.GetNormalizedRoles(rawRoles);
        normalized.Should().BeEquivalentTo("investigator", "external_monitor", "subject", "sponsor_mm");
    }

    [Fact]
    public void PermissionMatrix_EvaluatesResourceActionPermissions()
    {
        var principal = new Principal
        {
            UserId = "user-101",
            Roles = new[] { "investigator" },
            RawRoles = new[] { "site investigator" }
        };

        PermissionMatrix.HasPermission(principal, "subject_enrollment:create").Should().BeTrue();
        PermissionMatrix.HasPermission(principal, "subject_enrollment:read").Should().BeTrue();
        PermissionMatrix.HasPermission(principal, "tickets:comment").Should().BeTrue();
        PermissionMatrix.HasPermission(principal, "tickets:transition").Should().BeTrue();
        PermissionMatrix.HasPermission(principal, "tickets:sign").Should().BeTrue();
        PermissionMatrix.HasPermission(principal, "soa:delete").Should().BeFalse();

        var safetyPrincipal = new Principal
        {
            UserId = "safety-101",
            Roles = RoleNormalization.GetNormalizedRoles(new[] { "safety_reviewer" }),
            RawRoles = new[] { "safety_reviewer" }
        };
        PermissionMatrix.HasPermission(safetyPrincipal, "tickets:read").Should().BeTrue();
        PermissionMatrix.HasPermission(safetyPrincipal, "tickets:comment").Should().BeTrue();
        PermissionMatrix.HasPermission(safetyPrincipal, "tickets:transition").Should().BeTrue();
        PermissionMatrix.HasPermission(safetyPrincipal, "tickets:sign").Should().BeTrue();

        var auditorPrincipal = new Principal
        {
            UserId = "auditor-101",
            Roles = new[] { "auditor" },
            RawRoles = new[] { "auditor" }
        };
        PermissionMatrix.HasPermission(auditorPrincipal, "tickets:read").Should().BeTrue();
        PermissionMatrix.HasPermission(auditorPrincipal, "tickets:comment").Should().BeFalse();
        PermissionMatrix.HasPermission(auditorPrincipal, "tickets:transition").Should().BeFalse();
        PermissionMatrix.HasPermission(auditorPrincipal, "tickets:sign").Should().BeFalse();

        var sysadmin = new Principal
        {
            UserId = "admin-1",
            Roles = new[] { "sysadmin" },
            RawRoles = new[] { "sysadmin" }
        };
        PermissionMatrix.HasPermission(sysadmin, "soa:delete").Should().BeTrue();
    }

    [Fact]
    public void SiteAccessEvaluator_EnforcesFailClosedSiteScopedLogic()
    {
        // Fail-closed: site-scoped user with no assigned sites is denied
        var siteUserWithoutSites = new Principal
        {
            UserId = "crc-1",
            Roles = new[] { "crc" },
            AssignedSites = Array.Empty<string>()
        };
        SiteAccessEvaluator.CanAccessSite(siteUserWithoutSites, "SITE-101").Should().BeFalse();

        // Site-scoped user with assigned site
        var siteUserWithSites = new Principal
        {
            UserId = "crc-2",
            Roles = new[] { "crc" },
            AssignedSites = new[] { "SITE-101", "SITE-102" }
        };
        SiteAccessEvaluator.CanAccessSite(siteUserWithSites, "SITE-101").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(siteUserWithSites, "SITE-999").Should().BeFalse();

        // Unrestricted sponsor user
        var sponsorUser = new Principal
        {
            UserId = "dm-1",
            Roles = new[] { "sponsor_dm" },
            AssignedSites = Array.Empty<string>()
        };
        SiteAccessEvaluator.CanAccessSite(sponsorUser, "SITE-101").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(sponsorUser, null).Should().BeTrue();

        // Verified trial-* tenant: standalone trial_coordinator is unscoped when AssignedSites is empty
        ClinicalScopeGuard.IsVerifiedTrialTenant("trial-a1b2c3d4e5f6").Should().BeTrue();
        ClinicalScopeGuard.IsVerifiedTrialTenant("trial-A1B2C3D4E5F6").Should().BeFalse();
        ClinicalScopeGuard.IsVerifiedTrialTenant("tenant-enterprise-01").Should().BeFalse();

        var verifiedTrialCoord = new Principal
        {
            UserId = "tc-trial",
            TenantId = "trial-a1b2c3d4e5f6",
            Roles = new[] { "trial_coordinator" },
            AssignedSites = Array.Empty<string>()
        };
        ClinicalScopeGuard.IsSiteBound(verifiedTrialCoord).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(verifiedTrialCoord, "SITE-101").Should().BeTrue();

        // Non-trial tenant: standalone trial_coordinator is site-bound and fails closed without AssignedSites
        var enterpriseTrialCoordWithoutSites = new Principal
        {
            UserId = "tc-ent-unassigned",
            TenantId = "tenant-enterprise-01",
            Roles = new[] { "trial_coordinator" },
            AssignedSites = Array.Empty<string>()
        };
        ClinicalScopeGuard.IsSiteBound(enterpriseTrialCoordWithoutSites).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(enterpriseTrialCoordWithoutSites).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(enterpriseTrialCoordWithoutSites, "SITE-101").Should().BeFalse();

        // Non-trial tenant with explicit site assignment: restricted strictly to assigned sites
        var enterpriseTrialCoordWithSite = new Principal
        {
            UserId = "tc-ent-site",
            TenantId = "tenant-enterprise-01",
            Roles = new[] { "trial_coordinator" },
            AssignedSites = new[] { "SITE-101" }
        };
        ClinicalScopeGuard.IsSiteBound(enterpriseTrialCoordWithSite).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(enterpriseTrialCoordWithSite).Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(enterpriseTrialCoordWithSite, "SITE-101").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(enterpriseTrialCoordWithSite, "SITE-202").Should().BeFalse();

        // Multi-role union: enterprise caller holding Sponsor Admin + Trial Coordinator remains unscoped
        var enterpriseSponsorPlusTrialCoord = new Principal
        {
            UserId = "tc-ent-sponsor",
            TenantId = "tenant-enterprise-01",
            Roles = new[] { "sponsor_dm", "trial_coordinator" },
            RawRoles = new[] { "Sponsor Admin", "Trial Coordinator" },
            AssignedSites = Array.Empty<string>()
        };
        ClinicalScopeGuard.IsSiteBound(enterpriseSponsorPlusTrialCoord).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(enterpriseSponsorPlusTrialCoord, "SITE-101").Should().BeTrue();
    }

    [Theory]
    [InlineData(RoleNormalization.ROLE_PHARMACIST)]
    [InlineData(RoleNormalization.ROLE_SUBJECT)]
    [InlineData("unblinded_pharmacist")]
    [InlineData("patient")]
    [InlineData("epro")]
    [GxPRequirement("PRD-SYS-192")]
    public void SiteAccessEvaluator_AndClinicalScopeGuard_FailClosedForPharmacistAndSubjectRoles(string rawRole)
    {
        SiteAccessEvaluator.SITE_SCOPED_ROLES.Should().Contain(RoleNormalization.ROLE_PHARMACIST);
        SiteAccessEvaluator.SITE_SCOPED_ROLES.Should().Contain(RoleNormalization.ROLE_SUBJECT);

        var normalizedRoles = RoleNormalization.GetNormalizedRoles([rawRole]);
        var unassignedPrincipal = new Principal
        {
            UserId = $"unassigned-{rawRole}",
            Roles = normalizedRoles,
            RawRoles = [rawRole],
            AssignedSites = Array.Empty<string>(),
        };

        SiteAccessEvaluator.CanAccessSite(unassignedPrincipal, "SITE-99").Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(unassignedPrincipal, null).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(unassignedPrincipal, "   ").Should().BeFalse();
        ClinicalScopeGuard.IsSiteBound(unassignedPrincipal).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(unassignedPrincipal).Should().BeFalse();

        var assignedPrincipal = unassignedPrincipal with
        {
            UserId = $"assigned-{rawRole}",
            AssignedSites = ["SITE-99"],
        };

        SiteAccessEvaluator.CanAccessSite(assignedPrincipal, "SITE-99").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(assignedPrincipal, "site-99").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(assignedPrincipal, "SITE-101").Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(assignedPrincipal, null).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(assignedPrincipal, "   ").Should().BeFalse();
        ClinicalScopeGuard.IsSiteBound(assignedPrincipal).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(assignedPrincipal).Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public void SiteAccessEvaluator_DualSponsorAndPharmacistRoles_RestrictsToAssignedSites()
    {
        var unassignedDualRole = new Principal
        {
            UserId = "dual-unassigned",
            Roles = [RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_PHARMACIST],
            AssignedSites = Array.Empty<string>(),
        };

        SiteAccessEvaluator.CanAccessSite(unassignedDualRole, "SITE-99").Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(unassignedDualRole, null).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(unassignedDualRole, " ").Should().BeFalse();
        ClinicalScopeGuard.IsSiteBound(unassignedDualRole).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(unassignedDualRole).Should().BeFalse();

        var assignedDualRole = unassignedDualRole with
        {
            UserId = "dual-assigned",
            AssignedSites = ["SITE-01"],
        };

        SiteAccessEvaluator.CanAccessSite(assignedDualRole, "SITE-01").Should().BeTrue();
        SiteAccessEvaluator.CanAccessSite(assignedDualRole, "SITE-99").Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(assignedDualRole, null).Should().BeFalse();

        var trialDualPharmacist = new Principal
        {
            UserId = "trial-pharm-unassigned",
            TenantId = "trial-a1b2c3d4e5f6",
            Roles = [RoleNormalization.ROLE_TRIAL_COORDINATOR, RoleNormalization.ROLE_PHARMACIST],
            AssignedSites = Array.Empty<string>(),
        };

        ClinicalScopeGuard.IsSiteBound(trialDualPharmacist).Should().BeTrue();
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(trialDualPharmacist).Should().BeFalse();
        SiteAccessEvaluator.CanAccessSite(trialDualPharmacist, "SITE-99").Should().BeFalse();
    }

    [Fact]
    public void StudyAccessEvaluator_RestrictsStudyScopedUsers()
    {
        var externalMonitor = new Principal
        {
            UserId = "mon-1",
            Roles = new[] { "external_monitor" },
            AssignedStudies = new[] { "STUDY-A" }
        };

        StudyAccessEvaluator.CanAccessStudy(externalMonitor, "STUDY-A").Should().BeTrue();
        StudyAccessEvaluator.CanAccessStudy(externalMonitor, "STUDY-B").Should().BeFalse();

        var wildcardMonitor = new Principal
        {
            UserId = "mon-2",
            Roles = new[] { "external_monitor" },
            AssignedStudies = new[] { "*" }
        };
        StudyAccessEvaluator.CanAccessStudy(wildcardMonitor, "STUDY-B").Should().BeTrue();
    }

    /// <summary>Study scope fails closed: no assignment or no study id is a denial, whatever the role (ADR-2516).</summary>
    [Theory]
    [InlineData("sysadmin")]
    [InlineData("sponsor_dm")]
    [InlineData("investigator")]
    [GxPRequirement("PRD-SYS-192")]
    public void StudyAccessEvaluator_FailsClosed(string role)
    {
        var unassigned = new Principal { UserId = "user-1", Roles = [role] };
        var assigned = unassigned with { AssignedStudies = ["STUDY-A"] };

        StudyAccessEvaluator.CanAccessStudy(unassigned, "STUDY-A").Should().BeFalse();
        StudyAccessEvaluator.CanAccessStudy(assigned, null).Should().BeFalse();
        StudyAccessEvaluator.CanAccessStudy(assigned, " ").Should().BeFalse();
        StudyAccessEvaluator.CanAccessStudy(null, "STUDY-A").Should().BeFalse();
    }

    [Fact]
    public async Task AuditorPersona_RestrictedToReadOnlyAccess()
    {
        var middleware = new AuditorWriteGuardMiddleware();
        var auditor = new Principal { UserId = "auditor-01", Roles = ["auditor"] };

        // Auditor GET request (Read-only) -> Allowed
        var getContext = new DefaultHttpContext();
        getContext.Request.Method = "GET";
        getContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = auditor;

        bool getNextExecuted = false;
        await middleware.InvokeAsync(getContext, ctx =>
        {
            getNextExecuted = true;
            return Task.CompletedTask;
        });

        getNextExecuted.Should().BeTrue();
        getContext.Response.StatusCode.Should().Be(200);

        // Auditor POST request (Write operation) -> Blocked with 403 Forbidden
        var postContext = new DefaultHttpContext();
        postContext.Request.Method = "POST";
        postContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = auditor;
        postContext.Response.Body = new MemoryStream();

        bool postNextExecuted = false;
        await middleware.InvokeAsync(postContext, ctx =>
        {
            postNextExecuted = true;
            return Task.CompletedTask;
        });

        postNextExecuted.Should().BeFalse();
        postContext.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ExpiredSubscriptionWriteGuard_BlocksMutationsWith403ProblemDetails_AndExemptsReadsExportsSignaturesAndActivation()
    {
        var middleware = new ExpiredSubscriptionWriteGuardMiddleware();
        var expiredPrincipal = new Principal
        {
            UserId = "sponsor-admin-01",
            Roles = ["sponsor_dm"],
            TenantId = "trial-expired-01",
            TenantReadOnly = true,
        };

        // 1. Safe GET/HEAD requests pass through unimpeded.
        foreach (string safeMethod in new[] { HttpMethods.Get, HttpMethods.Head })
        {
            var safeContext = new DefaultHttpContext();
            safeContext.Request.Method = safeMethod;
            safeContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = expiredPrincipal;
            bool safeExecuted = false;
            await middleware.InvokeAsync(safeContext, _ =>
            {
                safeExecuted = true;
                return Task.CompletedTask;
            });
            safeExecuted.Should().BeTrue();
        }

        // 2. Standard clinical POST/PUT/PATCH/DELETE mutations fail with 403 application/problem+json (K.Subscription.Expired).
        foreach (string writeMethod in new[] { HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete })
        {
            var writeContext = new DefaultHttpContext();
            writeContext.Request.Method = writeMethod;
            writeContext.Request.Path = "/api/v1/ctms/site-milestones";
            writeContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = expiredPrincipal;
            writeContext.Response.Body = new MemoryStream();

            bool writeExecuted = false;
            await middleware.InvokeAsync(writeContext, _ =>
            {
                writeExecuted = true;
                return Task.CompletedTask;
            });

            writeExecuted.Should().BeFalse();
            writeContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            writeContext.Response.ContentType.Should().StartWith("application/problem+json");
            writeContext.Response.Body.Position = 0;
            using var doc = await JsonDocument.ParseAsync(writeContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            doc.RootElement.GetProperty("code").GetString().Should().Be(ExpiredSubscriptionWriteGuardMiddleware.SubscriptionExpiredCode);
            doc.RootElement.GetProperty("status").GetInt32().Should().Be(403);
        }

        // 3. Regulated eTMF export (Permission:etmf_document:export) and explicit closeout/activation endpoints
        //    (AllowExpiredTenantWriteMetadata) are exempted, whereas general ElectronicSignatureRequired routes without
        //    AllowExpiredTenantWriteMetadata are refused with 403 K.Subscription.Expired.
        Endpoint[] exemptedEndpoints =
        [
            new(_ => Task.CompletedTask, new EndpointMetadataCollection(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute($"{PermissionPolicyProvider.PolicyPrefix}etmf_document:export")), "etmf-export"),
            new(_ => Task.CompletedTask, new EndpointMetadataCollection(ElectronicSignatureRequired.Instance, AllowExpiredTenantWriteMetadata.Instance), "closeout-lock-sign"),
            new(_ => Task.CompletedTask, new EndpointMetadataCollection(AllowExpiredTenantWriteMetadata.Instance), "activate-subscription"),
        ];

        foreach (var endpoint in exemptedEndpoints)
        {
            var exemptContext = new DefaultHttpContext();
            exemptContext.Request.Method = HttpMethods.Post;
            exemptContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = expiredPrincipal;
            exemptContext.SetEndpoint(endpoint);

            bool exemptExecuted = false;
            await middleware.InvokeAsync(exemptContext, _ =>
            {
                exemptExecuted = true;
                return Task.CompletedTask;
            });

            exemptExecuted.Should().BeTrue($"endpoint '{endpoint.DisplayName}' must remain available when a workspace trial is expired");
        }

        var nonExemptSignedEndpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(ElectronicSignatureRequired.Instance),
            "library-item-approve");
        var blockedSignContext = new DefaultHttpContext();
        blockedSignContext.Request.Method = HttpMethods.Post;
        blockedSignContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = expiredPrincipal;
        blockedSignContext.SetEndpoint(nonExemptSignedEndpoint);
        blockedSignContext.Response.Body = new MemoryStream();

        bool nonExemptExecuted = false;
        await middleware.InvokeAsync(blockedSignContext, _ =>
        {
            nonExemptExecuted = true;
            return Task.CompletedTask;
        });
        nonExemptExecuted.Should().BeFalse("general ElectronicSignatureRequired endpoints without AllowExpiredTenantWriteMetadata must be blocked when expired");
        blockedSignContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void HmacSigner_BindsTenantReadOnlyIntoCanonicalSignature_AndRejectsTampering()
    {
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecret);
        var issuedAt = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

        var readOnlyIdentity = new GatewayIdentity(
            UserId: "usr-exp-01",
            Roles: ["Sponsor Admin"],
            SiteIds: [],
            TenantId: "trial-exp-tenant",
            StudyIds: ["*"],
            TenantReadOnly: true);

        var headers = readOnlyIdentity.ToSignedHeaders(issuedAt, secret);
        headers[GatewayHeaders.TenantReadOnly].Should().Be("true");
        headers[GatewayHeaders.TenantProductFamilies].Should().Be("none");

        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            studyIds: headers[GatewayHeaders.StudyIds],
            tenantId: headers[GatewayHeaders.TenantId],
            tenantReadOnly: true,
            tenantProductFamilies: headers[GatewayHeaders.TenantProductFamilies]).Should().BeTrue();

        // Tampering or stripping X-Tenant-Read-Only or X-Tenant-Product-Families fails HMAC verification immediately.
        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            studyIds: headers[GatewayHeaders.StudyIds],
            tenantId: headers[GatewayHeaders.TenantId],
            tenantReadOnly: false,
            tenantProductFamilies: headers[GatewayHeaders.TenantProductFamilies]).Should().BeFalse();

        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            studyIds: headers[GatewayHeaders.StudyIds],
            tenantId: headers[GatewayHeaders.TenantId],
            tenantReadOnly: true,
            tenantProductFamilies: "Blueprint,Capture").Should().BeFalse();
    }

    [Fact]
    public async Task ProductFamilyGuardMiddleware_EnforcesModuleEntitlements_AndAllowsBaselineRoutes()
    {
        var guard = new ProductFamilyGuardMiddleware();
        var individualPrincipal = new Principal
        {
            UserId = "crc-01",
            TenantId = "trial-ind-01",
            Roles = ["crc"],
            ProductFamilies = ["Blueprint", "Capture"],
        };

        // 1. Capture route succeeds on Individual workspace (Blueprint + Capture).
        var captureContext = new DefaultHttpContext();
        captureContext.Request.Path = "/api/v1/execution/subjects";
        captureContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = individualPrincipal;
        bool captureRan = false;
        await guard.InvokeAsync(captureContext, _ =>
        {
            captureRan = true;
            return Task.CompletedTask;
        });
        captureRan.Should().BeTrue();

        // 2. Orbit / Vigil / Assure routes fail closed with 403 K.Commercial.ProductFamilyNotEntitled on Individual workspace.
        foreach (string unentitledPath in new[] { "/api/v1/ctms/milestones", "/api/v1/org/tenants", "/api/v1/safety/cases", "/api/v1/quality/capas" })
        {
            var deniedContext = new DefaultHttpContext();
            deniedContext.Request.Path = unentitledPath;
            deniedContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = individualPrincipal;
            deniedContext.Response.Body = new MemoryStream();
            bool deniedRan = false;
            await guard.InvokeAsync(deniedContext, _ =>
            {
                deniedRan = true;
                return Task.CompletedTask;
            });
            deniedRan.Should().BeFalse();
            deniedContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            deniedContext.Response.Body.Position = 0;
            using var problem = await JsonDocument.ParseAsync(deniedContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            problem.RootElement.GetProperty("code").GetString().Should().Be("K.Commercial.ProductFamilyNotEntitled");
        }

        // 3. Baseline kernel/signature routes (/api/v1/kernel/entitlements, /api/v1/kernel/locks, /api/v1/kernel/enrollments/lookup, /api/v1/kernel/studies/.../exports, /api/v1/kernel/sites GET, /api/v1/kernel/studies/.../sites GET, /api/v1/signatures) always pass regardless of module entitlements.
        var zeroFamilyPrincipal = individualPrincipal with { ProductFamilies = [] };
        foreach (string baselinePath in new[]
        {
            "/api/v1/kernel/entitlements",
            "/api/v1/kernel/locks",
            "/api/v1/kernel/enrollments/lookup",
            "/api/v1/kernel/studies/study:1/exports",
            "/api/v1/kernel/sites",
            "/api/v1/kernel/studies/study:1/sites",
            "/api/v1/signatures",
        })
        {
            var baselineContext = new DefaultHttpContext();
            baselineContext.Request.Method = HttpMethods.Get;
            baselineContext.Request.Path = baselinePath;
            baselineContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = zeroFamilyPrincipal;
            bool baselineRan = false;
            await guard.InvokeAsync(baselineContext, _ =>
            {
                baselineRan = true;
                return Task.CompletedTask;
            });
            baselineRan.Should().BeTrue($"{baselinePath} is a regulated baseline route and must never be gated behind module ProductFamily entitlements");
        }

        // 4. Site setup writes (/api/v1/kernel/sites, /api/v1/kernel/studies/.../sites) succeed with Capture or Orbit even without Blueprint, and fail closed for Vigil-only callers.
        var captureOnlyPrincipal = individualPrincipal with { ProductFamilies = ["Capture"] };
        var vigilOnlyPrincipal = individualPrincipal with { ProductFamilies = ["Vigil"] };
        foreach (string siteWritePath in new[] { "/api/v1/kernel/sites", "/api/v1/kernel/studies/study:1/sites" })
        {
            var captureWriteCtx = new DefaultHttpContext();
            captureWriteCtx.Request.Method = HttpMethods.Post;
            captureWriteCtx.Request.Path = siteWritePath;
            captureWriteCtx.Items[GatewayAuthenticationHandler.PrincipalItemKey] = captureOnlyPrincipal;
            bool captureWriteRan = false;
            await guard.InvokeAsync(captureWriteCtx, _ =>
            {
                captureWriteRan = true;
                return Task.CompletedTask;
            });
            captureWriteRan.Should().BeTrue($"{siteWritePath} POST should be allowed for Capture-entitled workspaces");

            var vigilWriteCtx = new DefaultHttpContext();
            vigilWriteCtx.Request.Method = HttpMethods.Post;
            vigilWriteCtx.Request.Path = siteWritePath;
            vigilWriteCtx.Items[GatewayAuthenticationHandler.PrincipalItemKey] = vigilOnlyPrincipal;
            vigilWriteCtx.Response.Body = new MemoryStream();
            bool vigilWriteRan = false;
            await guard.InvokeAsync(vigilWriteCtx, _ =>
            {
                vigilWriteRan = true;
                return Task.CompletedTask;
            });
            vigilWriteRan.Should().BeFalse();
            vigilWriteCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }
    }

    [Fact]
    public async Task TenantScopingCaps_RoundTripsIsoAndNamedCountries_AndSubscriptionScopingGuardEnforcesCaps()
    {
        var isoCaps = new TenantScopingCaps("Phase II", 18, ["us", "DE", "NL"], 5, 120, 2);
        string isoWire = isoCaps.ToHeaderValue();
        isoWire.Should().Be("v1;phase=Phase%20II;months=18;sites=5;participants=120;studies=2;countries=DE,NL,US");
        TenantScopingCaps.TryParseHeader(isoWire, out var parsedIso).Should().BeTrue();
        parsedIso!.Countries.Should().Equal("DE", "NL", "US");
        parsedIso.StudyPhase.Should().Be("Phase II");

        var namedCaps = new TenantScopingCaps("Phase III", 36, ["United States", "Germany"], 12, 400, 4);
        string namedWire = namedCaps.ToHeaderValue();
        TenantScopingCaps.TryParseHeader(namedWire, out var parsedNamed).Should().BeTrue();
        parsedNamed!.Countries.Should().Equal("GERMANY", "UNITED STATES");
        parsedNamed.StudyPhase.Should().Be("Phase III");

        var guard = new SubscriptionScopingGuardMiddleware();
        var scopedPrincipal = new Principal
        {
            UserId = "pm-01",
            TenantId = "scoped-tenant-01",
            Roles = ["study_manager"],
            ProductFamilies = Cadence.Testing.Security.TestSecurityContext.AllProductFamilies,
            TenantScoping = isoCaps,
        };

        // Outside contracted country ("JP" not in DE,NL,US) -> 403 K.Commercial.ScopingCapExceeded
        var deniedCtx = new DefaultHttpContext();
        deniedCtx.Request.Method = HttpMethods.Post;
        deniedCtx.Request.Path = "/api/v1/ctms/sites";
        deniedCtx.Request.ContentType = "application/json";
        byte[] payloadBytes = Encoding.UTF8.GetBytes("""{"site_code":"SITE-JP","country":"JP"}""");
        deniedCtx.Request.Body = new MemoryStream(payloadBytes);
        deniedCtx.Request.ContentLength = payloadBytes.Length;
        deniedCtx.Response.Body = new MemoryStream();
        deniedCtx.Items[GatewayAuthenticationHandler.PrincipalItemKey] = scopedPrincipal;

        bool nextRan = false;
        await guard.InvokeAsync(deniedCtx, _ =>
        {
            nextRan = true;
            return Task.CompletedTask;
        });

        nextRan.Should().BeFalse();
        deniedCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        deniedCtx.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(deniedCtx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        problem.RootElement.GetProperty("code").GetString().Should().Be("K.Commercial.ScopingCapExceeded");
    }

    [Fact]
    public void HmacSigner_GeneratesAndVerifiesGatewaySignatures()
    {
        byte[] secret = Encoding.UTF8.GetBytes(TestSecret);
        string timestamp = SystemClock.Instance.UtcNow.UtcDateTime.ToString("o");

        string sig = HmacSigner.GenerateGatewaySignature(
            userId: "usr-01",
            roles: "investigator",
            timestamp: timestamp,
            secret: secret,
            siteId: "SITE-A",
            sponsorId: "SPONSOR-1",
            unblindedAccess: false,
            tenantId: "tenant-default"
        );

        sig.Should().NotBeNullOrWhiteSpace();

        bool isValid = HmacSigner.VerifyGatewaySignature(
            userId: "usr-01",
            roles: "investigator",
            timestamp: timestamp,
            signature: sig,
            secret: secret,
            siteId: "SITE-A",
            sponsorId: "SPONSOR-1",
            unblindedAccess: false,
            tenantId: "tenant-default"
        );

        isValid.Should().BeTrue();

        // Invalid signature verification
        bool isInvalid = HmacSigner.VerifyGatewaySignature(
            userId: "usr-01",
            roles: "investigator",
            timestamp: timestamp,
            signature: "invalid-signature-hex",
            secret: secret,
            siteId: "SITE-A"
        );

        isInvalid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false, "Corrected entry")]
    [InlineData("", true, "a<b & c>d 'quoted' \"dq\" \\slash\t\n")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.stepup", false, "IRB approval — 治験 Zürich – São Paulo")]
    [InlineData("sig-token-large", true, "")]
    public void HmacSigner_MatchesLegacySortedDictionaryCanonicalVectors(
        string? sigToken,
        bool unblindedAccess,
        string changeReason)
    {
        byte[] secret = Encoding.UTF8.GetBytes(TestSecret);
        const string timestamp = "2026-11-19T17:45:00.0000000+00:00";
        string effectiveReason = sigToken == "sig-token-large" ? new string('R', 1200) : changeReason;

        var legacyPayload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["change_reason"] = effectiveReason,
            ["roles"] = "CRA,Site Investigator",
            ["site_id"] = "SITE-101,SITE-102",
            ["sponsor_id"] = "SPONSOR-1",
            ["study_ids"] = "STUDY-A,STUDY-B",
            ["tenant_id"] = "tenant-default",
            ["timestamp"] = timestamp,
            ["unblinded_access"] = unblindedAccess,
            ["user_id"] = "usr-01",
        };
        if (sigToken is not null)
        {
            legacyPayload["sig_token"] = sigToken;
        }

        string expected = Cadence.Security.CanonicalSigner.GenerateCanonicalSignature(legacyPayload, secret);
        string actual = HmacSigner.GenerateGatewaySignature(
            userId: "usr-01",
            roles: "CRA,Site Investigator",
            timestamp: timestamp,
            secret: secret,
            changeReason: effectiveReason,
            siteId: "SITE-101,SITE-102",
            studyIds: "STUDY-A,STUDY-B",
            sponsorId: "SPONSOR-1",
            unblindedAccess: unblindedAccess,
            tenantId: "tenant-default",
            sigToken: sigToken);

        actual.Should().Be(expected);
        HmacSigner.CanonicalSerialize(new Dictionary<string, object?>(legacyPayload))
            .Should().Equal(Cadence.Security.CanonicalSigner.CanonicalSerialize(legacyPayload));
    }

    [Fact]
    public void HmacSigner_CanonicalSerialize_SortsNestedObjectsWithoutReflection()
    {
        using var parsedDoc = JsonDocument.Parse("{\"z\":\"é <&>\",\"a\":[1,true,null]}");
        var payload = new Dictionary<string, object?>
        {
            ["zeta"] = 42,
            ["alpha"] = new Dictionary<string, object?>
            {
                ["note"] = "a<b & c>d 'q'",
                ["arr"] = new object?[] { "治験", false, 3.5, null },
            },
            ["element"] = parsedDoc.RootElement,
            ["node"] = new JsonObject { ["b"] = 2, ["a"] = "x" },
            ["bravo"] = null,
        };

        byte[] actual = HmacSigner.CanonicalSerialize(payload);
        byte[] expected = Cadence.Security.CanonicalSigner.CanonicalSerialize(payload);

        Encoding.UTF8.GetString(actual).Should().Be(Encoding.UTF8.GetString(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("deadbeef")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void HmacSigner_VerifyGatewaySignature_AcceptsHexCaseAndRejectsMalformedOrTamperedInputs(string malformedSignature)
    {
        byte[] secret = Encoding.UTF8.GetBytes(TestSecret);
        const string timestamp = "2026-11-19T17:45:00.0000000+00:00";

        string sig = HmacSigner.GenerateGatewaySignature(
            userId: "usr-01",
            roles: "investigator",
            timestamp: timestamp,
            secret: secret,
            changeReason: "Initial",
            siteId: "SITE-A",
            studyIds: "STUDY-1",
            sponsorId: "SPONSOR-1",
            unblindedAccess: false,
            tenantId: "tenant-default",
            sigToken: "tok-1");

        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, sig.ToUpperInvariant(), secret,
            "Initial", "SITE-A", "STUDY-1", "SPONSOR-1", false, "tenant-default", "tok-1").Should().BeTrue();

        // Tampered scope/token fields, wrong secret, and malformed hex strings all return false without throwing.
        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, sig, secret,
            "Initial", "SITE-A", "STUDY-1", "SPONSOR-1", false, "tenant-default", sigToken: null).Should().BeFalse();
        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, sig, Encoding.UTF8.GetBytes("different-secret-32-chars-long-99999"), // pragma: allowlist secret
            "Initial", "SITE-A", "STUDY-1", "SPONSOR-1", false, "tenant-default", "tok-1").Should().BeFalse();
        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, malformedSignature, secret).Should().BeFalse();
        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, sig[..^1], secret).Should().BeFalse();
        HmacSigner.VerifyGatewaySignature(
            "usr-01", "investigator", timestamp, sig, []).Should().BeFalse();
    }

    [Fact]
    public void GatewayOptionsAndIdentity_CacheSecretBytesAndReuseFormattedHeaders()
    {
        var authOptions = new GatewayAuthenticationOptions { GatewaySecret = TestSecret };
        authOptions.GatewaySecretBytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(TestSecret));
        authOptions.GatewaySecret = null;
        authOptions.GatewaySecretBytes.IsEmpty.Should().BeTrue();

        var signingOptions = new GatewaySigningOptions { Secret = TestSecret };
        signingOptions.SecretBytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(TestSecret));
        signingOptions.Secret = string.Empty;
        signingOptions.SecretBytes.IsEmpty.Should().BeTrue();

        byte[] secret = GatewayIdentity.EncodeSecret(TestSecret);
        var issuedAt = new DateTimeOffset(2026, 11, 19, 17, 45, 0, TimeSpan.Zero);
        var identity = new GatewayIdentity(
            UserId: "usr-01",
            Roles: ["CRA", "Monitor"],
            SiteIds: ["SITE-1"],
            SponsorId: "SPONSOR-1",
            UnblindedAccess: true,
            TenantId: "tenant-1",
            ChangeReason: "IRB approval — 治験",
            SigToken: "sig-tok-1",
            StudyIds: ["STUDY-1", "STUDY-2"]);

        var headers = identity.ToSignedHeaders(issuedAt, secret);
        headers[GatewayHeaders.Roles].Should().Be("CRA,Monitor");
        headers[GatewayHeaders.SiteId].Should().Be("SITE-1");
        headers[GatewayHeaders.StudyIds].Should().Be("STUDY-1,STUDY-2");
        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: secret,
            changeReason: "IRB approval — 治験",
            siteId: headers[GatewayHeaders.SiteId],
            studyIds: headers[GatewayHeaders.StudyIds],
            sponsorId: headers[GatewayHeaders.SponsorId],
            unblindedAccess: true,
            tenantId: headers[GatewayHeaders.TenantId],
            sigToken: headers[GatewayHeaders.SigToken],
            tenantProductFamilies: headers[GatewayHeaders.TenantProductFamilies]).Should().BeTrue();
    }

    [Fact]
    public void HmacSigner_VerifyGatewaySignature_EliminatesPerRequestHeapAllocations()
    {
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecret);
        const string timestamp = "2026-11-19T17:45:00.0000000+00:00";
        string sig = HmacSigner.GenerateGatewaySignature(
            "usr-01", "CRA,Monitor", timestamp, secret, "IRB approval — 治験",
            "SITE-1", "STUDY-1", "SPONSOR-1", false, "tenant-1", "sig-tok-1");

        // Warm up the thread-cached Utf8JsonWriter and ArrayPool bucket on the current thread.
        for (int i = 0; i < 8; i++)
        {
            HmacSigner.VerifyGatewaySignature(
                "usr-01", "CRA,Monitor", timestamp, sig, secret, "IRB approval — 治験",
                "SITE-1", "STUDY-1", "SPONSOR-1", false, "tenant-1", "sig-tok-1").Should().BeTrue();
        }

        const int iterations = 200;
        bool allValid = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            allValid &= HmacSigner.VerifyGatewaySignature(
                "usr-01", "CRA,Monitor", timestamp, sig, secret, "IRB approval — 治験",
                "SITE-1", "STUDY-1", "SPONSOR-1", false, "tenant-1", "sig-tok-1");
        }

        long allocatedPerCall = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        allValid.Should().BeTrue();
        allocatedPerCall.Should().BeLessThan(64, "VerifyGatewaySignature should stream to pooled UTF-8 buffers without per-call heap allocations");
    }

    /// <summary>
    /// Issue #4765: the audit HMAC key has no hardcoded fallback. Environment variables are process-wide, so
    /// this test reads AUDIT_LOG_SECRET_KEY rather than mutating it.
    /// </summary>
    [Fact]
    public void AuditSecretKey_WithoutExplicitKeyOrEnvironment_FailsClosed()
    {
        string? configured = Environment.GetEnvironmentVariable(AuditSecretKey.EnvironmentVariable);

        Func<string> resolve = () => AuditSecretKey.Resolve();
        Action construct = () => _ = new AuditDigestEngine(new InMemoryAuditStore());

        if (string.IsNullOrWhiteSpace(configured))
        {
            resolve.Should().Throw<InvalidOperationException>().WithMessage("*AUDIT_LOG_SECRET_KEY*");
            construct.Should().Throw<InvalidOperationException>().WithMessage("*AUDIT_LOG_SECRET_KEY*");
        }
        else
        {
            resolve().Should().Be(configured.Trim());
        }

        AuditSecretKey.Resolve("  explicit-key  ").Should().Be("explicit-key");
    }

    /// <summary>
    /// Issue #4765: the security and compliance digest calculators share one key source and one canonical
    /// payload, so a chain written by one verifies with the other.
    /// </summary>
    [Fact]
    public void AuditDigestCalculators_InSecurityAndCompliance_ProduceIdenticalDigests()
    {
        string security = AuditDigestCalculator.ComputeAuditDigest(
            "evt-1", "execution", "CREATE", "Subject", "S-1", "crc-01", "tenant-1", "Initial", "2026-09-29T00:00:00Z", AuditLogRecord.GENESIS_BLOCK, "shared-key");
        string compliance = Cadence.Compliance.AuditDigestCalculator.ComputeAuditDigest(
            "evt-1", "execution", "CREATE", "Subject", "S-1", "crc-01", "tenant-1", "Initial", "2026-09-29T00:00:00Z", AuditLogRecord.GENESIS_BLOCK, "shared-key");

        compliance.Should().Be(security);
    }

    [Fact]
    public void AuditDigestEngine_GeneratesMerkleChain_AndVerifiesIntegrity()
    {
        var store = new InMemoryAuditStore();
        var engine = new AuditDigestEngine(store, secretKey: "audit-secret-123");

        var payload1 = new AuditLogPayload
        {
            ServiceName = "execution",
            ActionType = "CREATE",
            EntityName = "ClinicalObservation",
            EntityId = "OBS-001",
            UserId = "crc-01",
            ReasonForChange = "Initial entry"
        };

        var record1 = engine.LogEvent(payload1);
        record1.PreviousDigest.Should().Be(AuditLogRecord.GENESIS_BLOCK);
        record1.Sha256Digest.Should().NotBeNullOrWhiteSpace();

        var payload2 = new AuditLogPayload
        {
            ServiceName = "execution",
            ActionType = "UPDATE",
            EntityName = "ClinicalObservation",
            EntityId = "OBS-001",
            UserId = "crc-01",
            ReasonForChange = "Corrected temperature value"
        };

        var record2 = engine.LogEvent(payload2);
        record2.PreviousDigest.Should().Be(record1.Sha256Digest);

        engine.VerifyChainIntegrity().Should().BeTrue();
    }

    [Fact]
    public void FieldMasker_ObfuscatesBlindedFields_AndUnmasksRtsmRoles()
    {
        var payload = new Dictionary<string, object?>
        {
            ["ssn"] = "123-45-6789",
            ["treatment_arm"] = "ARM-A",
            ["drug_code"] = "DRUG-101",
            ["subject_id"] = "SUBJ-001"
        };

        // Blinded investigator -> ssn, treatment_arm, drug_code masked
        var investigator = new Principal
        {
            UserId = "inv-01",
            Roles = new[] { "investigator" },
            UnblindedAccess = false
        };

        var maskedObj = FieldMasker.MaskPayload(payload, investigator) as IDictionary<string, object?>;
        maskedObj.Should().NotBeNull();
        maskedObj!["ssn"].Should().Be("***-**-****");
        maskedObj["treatment_arm"].Should().Be("BLINDED");
        maskedObj["drug_code"].Should().Be("Obfuscated Kit");
        maskedObj["subject_id"].Should().Be("SUBJ-001");

        // Pharmacist -> drug_code unmasked, treatment_arm masked
        var pharmacist = new Principal
        {
            UserId = "pharm-01",
            Roles = new[] { "pharmacist" },
            UnblindedAccess = false
        };

        var pharmObj = FieldMasker.MaskPayload(payload, pharmacist) as IDictionary<string, object?>;
        pharmObj!["drug_code"].Should().Be("DRUG-101");
        pharmObj["treatment_arm"].Should().Be("BLINDED");

        // Unblinded statistician -> treatment_arm unmasked, drug_code masked
        var stat = new Principal
        {
            UserId = "stat-01",
            Roles = new[] { "unblinded_statistician" },
            UnblindedAccess = false
        };

        var statObj = FieldMasker.MaskPayload(payload, stat) as IDictionary<string, object?>;
        statObj!["treatment_arm"].Should().Be("ARM-A");
        statObj["drug_code"].Should().Be("Obfuscated Kit");
    }

    [Fact]
    public void FieldMasker_MasksUnblindedAuditRationale_ForNonUnblindedRoles_AndUnmasksForRtsmRoles()
    {
        var result = new SubjectAllocationResult(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            AssignedArm: "ARM-A",
            StratumKey: "STRATUM-1",
            AlgorithmType: RandomizationAlgorithmType.RESPONSE_ADAPTIVE,
            AllocationIndex: 1,
            DeterministicChecksum: "abc123hash",
            DecisionRationale: "Assigned to arm 'ARM-A' via Bayesian Response-Adaptive Randomization in stratum 'STRATUM-1' under Adaptive Phase.",
            UnblindedAuditRationale: "Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]]."
        );

        var investigator = new Principal
        {
            UserId = "inv-01",
            Roles = new[] { "investigator" },
            UnblindedAccess = false
        };

        var maskedDict = FieldMasker.MaskPayload(result, investigator) as IDictionary<string, object?>;
        maskedDict.Should().NotBeNull();
        maskedDict!["UnblindedAuditRationale"].Should().Be("MASKED");
        maskedDict!["DecisionRationale"].Should().Be("Assigned to arm 'ARM-A' via Bayesian Response-Adaptive Randomization in stratum 'STRATUM-1' under Adaptive Phase.");

        var statistician = new Principal
        {
            UserId = "stat-01",
            Roles = new[] { "unblinded_statistician" },
            UnblindedAccess = false
        };

        var statDict = FieldMasker.MaskPayload(result, statistician) as IDictionary<string, object?>;
        statDict.Should().NotBeNull();
        statDict!["UnblindedAuditRationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");

        var idmc = new Principal
        {
            UserId = "idmc-01",
            Roles = new[] { "idmc" },
            UnblindedAccess = false
        };

        var idmcDict = FieldMasker.MaskPayload(result, idmc) as IDictionary<string, object?>;
        idmcDict.Should().NotBeNull();
        idmcDict!["UnblindedAuditRationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");

        var emergency = new Principal
        {
            UserId = "em-01",
            Roles = new[] { "emergency_unblinder" },
            UnblindedAccess = false
        };

        var emDict = FieldMasker.MaskPayload(result, emergency) as IDictionary<string, object?>;
        emDict.Should().NotBeNull();
        emDict!["UnblindedAuditRationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");
    }

    [Fact]
    public void DependencyInjection_RegistersCadenceSecurityServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCadenceSecurity(opts =>
        {
            opts.GatewaySecret = TestSecret;
            opts.AuditLogSecretKey = "audit-secret-for-di-test"; // pragma: allowlist secret
        });

        var provider = services.BuildServiceProvider();

        provider.GetService<AuditorWriteGuardMiddleware>().Should().NotBeNull();
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<GatewayAuthenticationOptions>>()
            .Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret.Should().Be(TestSecret);
        provider.GetService<SigTokenVerifier>().Should().NotBeNull();
        provider.GetService<AuditDigestEngine>().Should().NotBeNull();
        provider.GetService<IAuditLogWriter>().Should().NotBeNull();
    }

    [Fact]
    public void CallerScope_PrefersGatewayPrincipalItemKey_OverClaimsPrincipal()
    {
        var context = new DefaultHttpContext();
        var gatewayPrincipal = new Principal
        {
            UserId = "usr-gateway-01",
            TenantId = "tenant_alpha",
            Roles = ["sponsor_dm"],
            AssignedStudies = ["STUDY-GW"],
            AssignedSites = ["SITE-GW"]
        };
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = gatewayPrincipal;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "usr-claims-fallback"),
            new Claim(ClaimTypes.Role, "crc"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-CLAIMS")
        ], authenticationType: "TestAuth"));

        var resolved = CallerScope.Principal(context);
        resolved.Should().BeSameAs(gatewayPrincipal);
        CallerScope.VerifiedUserId(context).Should().Be("usr-gateway-01");
        CallerScope.RequiredPrincipal(context).Should().BeSameAs(gatewayPrincipal);
        CallerScope.RequiredUserId(context).Should().Be("usr-gateway-01");
    }

    [Fact]
    public void CallerScope_FallsBackToAuthenticatedClaimsPrincipal_AndRejectsUnauthenticatedOrMissingSub()
    {
        var unauthenticated = new DefaultHttpContext();
        unauthenticated.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "usr-unauth")
        ]));

        CallerScope.Principal(unauthenticated).Should().BeNull();
        CallerScope.VerifiedUserId(unauthenticated).Should().BeNull();
        CallerScope.VerifiedUserId(null).Should().BeNull();

        var missingSub = new DefaultHttpContext();
        missingSub.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "name-id-only"),
            new Claim(ClaimTypes.Role, "crc")
        ], authenticationType: "TestAuth"));

        CallerScope.Principal(missingSub).Should().BeNull();
        var act = () => CallerScope.RequiredUserId(missingSub, "Custom missing caller message.");
        act.Should().Throw<InvalidOperationException>().WithMessage("Custom missing caller message.");

        var authenticated = new DefaultHttpContext();
        authenticated.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "usr-claims-01"),
            new Claim(CadenceClaimTypes.TenantId, " tenant_beta "),
            new Claim(ClaimTypes.Role, "Site Investigator"),
            new Claim("role", "finance_officer"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-100"),
            new Claim(CadenceClaimTypes.SiteId, "Site-101"),
            new Claim(CadenceClaimTypes.UnblindedAccess, "true")
        ], authenticationType: "TestAuth"));

        var principal = CallerScope.Principal(authenticated);
        principal.Should().NotBeNull();
        principal!.UserId.Should().Be("usr-claims-01");
        principal.TenantId.Should().Be("tenant_beta");
        principal.Roles.Should().Contain(["investigator", "finance_officer"]);
        principal.AssignedStudies.Should().Equal("STUDY-100");
        principal.AssignedSites.Should().Equal("Site-101");
        principal.UnblindedAccess.Should().BeTrue();
        CallerScope.Roles(authenticated, 12).Should().HaveLength(12);
        CallerScope.ResolveActor(authenticated).Should().Be(("usr-claims-01", "investigator,finance_officer"));
        CallerScope.ResolveActor(unauthenticated, defaultUser: "system", defaultRole: "SYSTEM").Should().Be(("system", "SYSTEM"));
    }

    [Fact]
    public void ClinicalScopeGuard_ResolveSiteFilter_AndApplyFilters_MatchMixedCaseAndTrimmedSites()
    {
        var context = new DefaultHttpContext();
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-01",
            TenantId = "tenant_alpha",
            Roles = ["crc"],
            AssignedStudies = ["STUDY-01"],
            AssignedSites = [" Site-101 "]
        };

        ClinicalScopeGuard.ResolveSiteFilter(context, null, out var allowedSites, out var errorResult)
            .Should().BeTrue();
        errorResult.Should().BeNull();
        allowedSites.Should().NotBeNull();
        allowedSites!.Contains("site-101").Should().BeTrue();
        allowedSites.Contains("SITE-101").Should().BeTrue();

        var singleErr = ClinicalScopeGuard.ResolveSingleSiteFilter(
            context,
            null,
            out var pushdownSites,
            out var effectiveSingleSiteId,
            code: ClinicalScopeGuard.ForbiddenCode);
        singleErr.Should().BeNull();
        pushdownSites.Should().NotBeNull();
        effectiveSingleSiteId.Should().Be("Site-101");

        var records = new[]
        {
            (StudyId: " STUDY-01 ", SiteId: " site-101 ", Id: "match-lower"),
            (StudyId: "STUDY-01", SiteId: "SITE-101", Id: "match-upper"),
            (StudyId: "STUDY-01", SiteId: "site-202", Id: "other-site"),
            (StudyId: "STUDY-02", SiteId: "site-101", Id: "other-study")
        };

        ClinicalScopeGuard.ResolveStudyFilter(context, null, out var allowedStudies, out _).Should().BeTrue();
        var filtered = ClinicalScopeGuard.ApplyFilters(
            records,
            allowedStudies,
            allowedSites,
            r => r.StudyId,
            r => r.SiteId).Select(r => r.Id).ToList();

        filtered.Should().Equal("match-lower", "match-upper");
    }

    [Fact]
    public void ClinicalScopeGuard_ForbiddenAndOutsideStudy_ReturnTypedProblemHttpResult()
    {
        ProblemHttpResult forbidden = ClinicalScopeGuard.Forbidden(
            "Site denied.",
            code: ClinicalScopeGuard.ForbiddenCode);
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        forbidden.ProblemDetails.Title.Should().Be("Forbidden");
        forbidden.ProblemDetails.Detail.Should().Be("Site denied.");
        forbidden.ProblemDetails.Extensions["code"].Should().Be("FORBIDDEN_SCOPE");

        ProblemHttpResult outside = ClinicalScopeGuard.OutsideStudy();
        outside.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outside.ProblemDetails.Title.Should().Be(ClinicalScopeGuard.OutsideStudyTitle);
        outside.ProblemDetails.Detail.Should().Be(ClinicalScopeGuard.OutsideStudyDeniedDetail);
        outside.ProblemDetails.Extensions.Should().NotContainKey("code");
    }

    private sealed record ScopedRow(string Id, string? StudyId, string? SiteId);

    [Fact]
    public void ClinicalScopeGuard_NarrowToScope_NarrowsQueryableCaseInsensitively_AndFailsClosed()
    {
        var source = new[]
        {
            new ScopedRow("r1", "STUDY-01", "site-101"),
            new ScopedRow("r2", "STUDY-01", " SITE-101 "),
            new ScopedRow("r3", "STUDY-01", "SITE-202"),
            new ScopedRow("r4", "STUDY-02", "SITE-101"),
        }.AsQueryable();

        var siteScopedContext = new DefaultHttpContext();
        siteScopedContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "crc-claims"),
            new Claim(ClaimTypes.Role, "crc"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-01"),
            new Claim(CadenceClaimTypes.SiteId, "Site-101")
        ], authenticationType: "TestAuth"));

        ClinicalScopeGuard.IsUnrestricted(siteScopedContext).Should().BeFalse();
        ClinicalScopeGuard.NarrowToScope(source, siteScopedContext, r => r.StudyId, r => r.SiteId)
            .Select(r => r.Id)
            .Should().Equal("r1", "r2");

        ClinicalScopeGuard.NarrowToStudyScope(source, siteScopedContext, r => r.StudyId)
            .Select(r => r.Id)
            .Should().Equal("r1", "r2", "r3");

        var unrestrictedContext = new DefaultHttpContext();
        unrestrictedContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "dm-01",
            Roles = ["sponsor_dm"],
            AssignedStudies = ["*"]
        };

        ClinicalScopeGuard.IsUnrestricted(unrestrictedContext).Should().BeTrue();
        ClinicalScopeGuard.NarrowToScope(source, unrestrictedContext, r => r.StudyId, r => r.SiteId)
            .Select(r => r.Id)
            .Should().Equal("r1", "r2", "r3", "r4");

        var anonymousContext = new DefaultHttpContext();
        ClinicalScopeGuard.NarrowToScope(source, anonymousContext, r => r.StudyId, r => r.SiteId)
            .Should().BeEmpty();

        var verifiedTrialCoordContext = new DefaultHttpContext();
        verifiedTrialCoordContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "tc-trial-01",
            TenantId = "trial-a1b2c3d4e5f6",
            Roles = ["trial_coordinator"],
            AssignedStudies = ["*"]
        };
        ClinicalScopeGuard.IsUnrestricted(verifiedTrialCoordContext).Should().BeTrue();
        ClinicalScopeGuard.NarrowToScope(source, verifiedTrialCoordContext, r => r.StudyId, r => r.SiteId)
            .Select(r => r.Id)
            .Should().Equal("r1", "r2", "r3", "r4");

        var enterpriseTrialCoordContext = new DefaultHttpContext();
        enterpriseTrialCoordContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "tc-ent-01",
            TenantId = "tenant-enterprise-01",
            Roles = ["trial_coordinator"],
            AssignedStudies = ["*"]
        };
        ClinicalScopeGuard.IsUnrestricted(enterpriseTrialCoordContext).Should().BeFalse();
        ClinicalScopeGuard.NarrowToScope(source, enterpriseTrialCoordContext, r => r.StudyId, r => r.SiteId)
            .Should().BeEmpty();
    }

    [Fact]
    public void ClinicalScopeGuard_RequireVerifiedTenant_RejectsGatewayPrincipalWithoutTenant_AndAllowsClaimsFallback()
    {
        var tenantlessGatewayContext = new DefaultHttpContext();
        tenantlessGatewayContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-no-tenant",
            TenantId = null,
            Roles = ["crc"],
            AssignedStudies = ["STUDY-01"],
            AssignedSites = ["SITE-101"]
        };

        var denied = ClinicalScopeGuard.RequireVerifiedTenant(tenantlessGatewayContext, ClinicalScopeGuard.ForbiddenCode);
        denied.Should().NotBeNull();
        denied!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        denied.ProblemDetails.Detail.Should().Be(ClinicalScopeGuard.TenantAccessDeniedDetail);
        denied.ProblemDetails.Extensions["code"].Should().Be(ClinicalScopeGuard.ForbiddenCode);

        ClinicalScopeGuard.RequireStudy(tenantlessGatewayContext, "STUDY-01", ClinicalScopeGuard.ForbiddenCode)
            .Should().BeNull();
        ClinicalScopeGuard.RequireStudy(tenantlessGatewayContext, "STUDY-01", ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            .Should().NotBeNull();
        ClinicalScopeGuard.ResolveStudyFilter(tenantlessGatewayContext, "STUDY-01", out _, out var studyErr, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            .Should().BeFalse();
        studyErr.Should().NotBeNull();
        ClinicalScopeGuard.ResolveSiteFilter(tenantlessGatewayContext, "SITE-101", out _, out var siteErr, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            .Should().BeFalse();
        siteErr.Should().NotBeNull();

        var verifiedGatewayContext = new DefaultHttpContext();
        verifiedGatewayContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-tenant",
            TenantId = "tenant_alpha",
            Roles = ["crc"],
            AssignedStudies = ["STUDY-01"],
            AssignedSites = ["SITE-101"]
        };

        ClinicalScopeGuard.RequireVerifiedTenant(verifiedGatewayContext, ClinicalScopeGuard.ForbiddenCode).Should().BeNull();
        ClinicalScopeGuard.RequireStudyAndSite(verifiedGatewayContext, "STUDY-01", "SITE-101", ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            .Should().BeNull();

        var claimsOnlyContext = new DefaultHttpContext();
        claimsOnlyContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "crc-claims-only"),
            new Claim(ClaimTypes.Role, "crc"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-01"),
            new Claim(CadenceClaimTypes.SiteId, "SITE-101")
        ], authenticationType: "TestAuth"));

        ClinicalScopeGuard.RequireVerifiedTenant(claimsOnlyContext, ClinicalScopeGuard.ForbiddenCode).Should().BeNull();
        ClinicalScopeGuard.RequireStudyAndSite(claimsOnlyContext, "STUDY-01", "SITE-101", ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            .Should().BeNull();
    }

    [Fact]
    public async Task AddCadenceSecurity_MinimalHostWithMissingAuditLogSecretKey_ThrowsOptionsValidationExceptionDuringStartAsync()
    {
        var secBuilder = WebApplication.CreateBuilder();
        secBuilder.WebHost.UseTestServer();
        secBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey] = TestSecurityContext.GatewaySecret,
            [AuditSecretKey.EnvironmentVariable] = null,
        });
        secBuilder.Services.AddCadenceSecurity();
        await using var secApp = secBuilder.Build();
        secApp.MapGet("/healthz", () => TypedResults.Ok()).AllowAnonymous();

        Func<Task> start = () => secApp.StartAsync(TestContext.Current.CancellationToken);

        var ex = (await start.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Message.Should().Contain(AuditSecretKey.EnvironmentVariable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-audit-key")]
    [InlineData("   short-key   ")]
    public async Task AddCadenceSecurity_WithBlankOrShortAuditLogSecretKey_RejectsStartupWithoutExposingSecret(string invalidSecret)
    {
        var secBuilder = WebApplication.CreateBuilder();
        secBuilder.WebHost.UseTestServer();
        secBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey] = TestSecurityContext.GatewaySecret,
            [AuditSecretKey.EnvironmentVariable] = invalidSecret,
        });
        secBuilder.Services.AddCadenceSecurity();
        await using var secApp = secBuilder.Build();

        Func<Task> start = () => secApp.StartAsync(TestContext.Current.CancellationToken);

        var ex = (await start.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Message.Should().Contain(AuditSecretKey.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(invalidSecret))
        {
            ex.Message.Should().NotContain(invalidSecret.Trim());
        }
    }

    [Fact]
    public async Task AddCadenceSecurity_ExplicitBlankOrShortCallbackKey_DoesNotFallBackToConfigurationAndFailsStartup()
    {
        var secBuilder = WebApplication.CreateBuilder();
        secBuilder.WebHost.UseTestServer();
        secBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey] = TestSecurityContext.GatewaySecret,
            [AuditSecretKey.EnvironmentVariable] = TestSecurityContext.AuditLogSecretKey,
        });
        secBuilder.Services.AddCadenceSecurity(opts => opts.AuditLogSecretKey = "too-short"); // pragma: allowlist secret
        await using var secApp = secBuilder.Build();

        Func<Task> start = () => secApp.StartAsync(TestContext.Current.CancellationToken);

        var ex = (await start.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Message.Should().Contain(AuditSecretKey.EnvironmentVariable)
            .And.NotContain("too-short");
    }

    [Fact]
    public void AddCadenceSecurity_BindsConfigurationSecret_AndConstructsAuditDigestEngineFromValidatedOptions()
    {
        const string configuredKey = "  configured-audit-hmac-secret-16+  "; // pragma: allowlist secret
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey] = TestSecret,
                [AuditSecretKey.EnvironmentVariable] = configuredKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCadenceSecurity();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CadenceSecurityOptions>>().Value;
        options.AuditLogSecretKey.Should().Be(configuredKey);

        var engine = provider.GetRequiredService<AuditDigestEngine>();
        var record = engine.LogEvent(new AuditLogPayload
        {
            ServiceName = "designer",
            ActionType = "CREATE",
            EntityName = "ProtocolDraft",
            EntityId = "PD-1",
            UserId = "designer-01",
            TenantId = "tenant-01",
            ReasonForChange = "Initial protocol draft",
        });

        string expected = AuditDigestCalculator.ComputeAuditDigest(
            record.EventId,
            record.ServiceName,
            record.ActionType,
            record.EntityName,
            record.EntityId,
            record.UserId,
            record.TenantId,
            record.ReasonForChange,
            record.Timestamp.ToString("o"),
            record.PreviousDigest,
            configuredKey.Trim());
        record.Sha256Digest.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short-secret-15")]
    public void DesignerHostStartup_WithoutValidAuditLogSecretKey_FailsFast(string invalidKey)
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Designer.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseSetting(AuditSecretKey.EnvironmentVariable, invalidKey));

        var ex = unconfigured.ValidateStartup().Should().BeOfType<OptionsValidationException>().Which;
        ex.Message.Should().Contain(AuditSecretKey.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(invalidKey))
        {
            ex.Message.Should().NotContain(invalidKey);
        }

        using var configured = new StartupValidationFactory<Cadence.Apps.Designer.Program>(builder => builder
            .UseTestGatewaySecret());
        configured.ValidateStartup().Should().BeNull();
    }
}
