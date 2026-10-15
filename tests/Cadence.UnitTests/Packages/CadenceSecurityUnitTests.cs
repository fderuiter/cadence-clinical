using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Cadence.Core.Domain.Randomization;
using Cadence.Kernel.Time;
using Cadence.Security.Audit;
using Cadence.Security.Authentication;
using Cadence.Security.Blinding;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

        var rawRoles = new[] { "site investigator", "CRO Monitor", "PATIENT" };
        var normalized = RoleNormalization.GetNormalizedRoles(rawRoles);
        normalized.Should().BeEquivalentTo("investigator", "external_monitor", "subject");
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
        PermissionMatrix.HasPermission(principal, "soa:delete").Should().BeFalse();

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

    [Fact]
    public void SigTokenVerifier_ValidatesAndConsumesToken_AndPreventsReplay()
    {
        byte[] secret = Encoding.UTF8.GetBytes(TestSecret);
        var replayCache = new ReplayCache();
        var verifier = new SigTokenVerifier(replayCache);

        // Generate JWT
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("sub", "user-777") }),
            Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        string sigToken = tokenHandler.WriteToken(token);

        // First verification -> Success
        var jwt = verifier.VerifyAndConsumeSigToken(sigToken, "user-777", secret);
        jwt.Should().NotBeNull();
        jwt.Subject.Should().Be("user-777");

        // Second verification (Replay attempt) -> Throws UnauthorizedAccessException
        Action replayAct = () => verifier.VerifyAndConsumeSigToken(sigToken, "user-777", secret);
        replayAct.Should().Throw<UnauthorizedAccessException>();
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
        maskedDict!["unblinded_audit_rationale"].Should().Be("MASKED");
        maskedDict!["decision_rationale"].Should().Be("Assigned to arm 'ARM-A' via Bayesian Response-Adaptive Randomization in stratum 'STRATUM-1' under Adaptive Phase.");

        var statistician = new Principal
        {
            UserId = "stat-01",
            Roles = new[] { "unblinded_statistician" },
            UnblindedAccess = false
        };

        var statDict = FieldMasker.MaskPayload(result, statistician) as IDictionary<string, object?>;
        statDict.Should().NotBeNull();
        statDict!["unblinded_audit_rationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");

        var idmc = new Principal
        {
            UserId = "idmc-01",
            Roles = new[] { "idmc" },
            UnblindedAccess = false
        };

        var idmcDict = FieldMasker.MaskPayload(result, idmc) as IDictionary<string, object?>;
        idmcDict.Should().NotBeNull();
        idmcDict!["unblinded_audit_rationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");

        var emergency = new Principal
        {
            UserId = "em-01",
            Roles = new[] { "emergency_unblinder" },
            UnblindedAccess = false
        };

        var emDict = FieldMasker.MaskPayload(result, emergency) as IDictionary<string, object?>;
        emDict.Should().NotBeNull();
        emDict!["unblinded_audit_rationale"].Should().Be("Arm Metrics: [ARM-A: n=10, y=8, Beta(9.0,3.0), V=0.9500, p=0.8000 [ACTIVE]].");
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
}
