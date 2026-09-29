using System;
using System.Text;

using FluentAssertions;
using Xunit;

using Cadence.Security;
using Cadence.Testing;
using Cadence.Testing.Security;

namespace Cadence.UnitTests.Testing;

public class SecurityContextTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TestSecurityContextFactory_CreatesValidContext()
    {
        var context = TestSecurityContextFactory.Create(
            userId: "investigator-101",
            roles: new[] { "Principal Investigator" },
            tenantId: "tenant_site101"
        );

        context.UserId.Should().Be("investigator-101");
        context.Roles.Should().Contain("Principal Investigator");
        context.TenantId.Should().Be("tenant_site101");
        context.AuthLevel.Should().Be("authenticated");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TestSecurityContextFactory_GeneratesAuthHeadersWithValidSignature()
    {
        byte[] secret = Encoding.UTF8.GetBytes("internal-gateway-secret-12345");
        var headers = TestSecurityContextFactory.CreateTestAuthHeaders(
            userId: "cra.monitor@example.com",
            roles: new[] { "CRA Monitor", "Data Manager" },
            tenantId: "tenant_default",
            changeReason: "Testing HMAC security generation",
            secret: secret
        );

        headers.Should().ContainKey("X-User-Id").WhoseValue.Should().Be("cra.monitor@example.com");
        headers.Should().ContainKey("X-User-Roles").WhoseValue.Should().Be("CRA Monitor,Data Manager");
        headers.Should().ContainKey("X-Tenant-Id").WhoseValue.Should().Be("tenant_default");
        headers.Should().ContainKey("X-Change-Reason").WhoseValue.Should().Be("Testing HMAC security generation");
        headers.Should().ContainKey("X-Gateway-Signature").WhoseValue.Should().NotBeNullOrEmpty();
        headers.Should().ContainKey("X-Signature-Version").WhoseValue.Should().Be("2");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void GenerateGatewaySignature_ProducesDeterministicHMACSignature()
    {
        byte[] secret = Encoding.UTF8.GetBytes("test-secret-key");
        string sig1 = TestSecurityContextFactory.GenerateGatewaySignature(
            userId: "user-001",
            roles: "Admin",
            timestamp: "1700000000",
            secret: secret,
            changeReason: "Data entry fix",
            tenantId: "tenant_1"
        );

        string sig2 = TestSecurityContextFactory.GenerateGatewaySignature(
            userId: "user-001",
            roles: "Admin",
            timestamp: "1700000000",
            secret: secret,
            changeReason: "Data entry fix",
            tenantId: "tenant_1"
        );

        sig1.Should().Be(sig2);
        sig1.Should().HaveLength(64); // Hex string length for SHA-256
    }
}
