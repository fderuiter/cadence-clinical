using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class SiteGreenlightStateMachineTests
{
    private static readonly DateTimeOffset TestTimestamp = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private const string Secret = "test-gateway-secret-32-chars-long-12345";

    private static CtmsSiteGreenlightGate CreateCompliantGate(string siteId = "SITE-001", SiteGreenlightStatus initialStatus = SiteGreenlightStatus.Pending)
    {
        return new CtmsSiteGreenlightGate("gate-" + siteId, initialStatus)
        {
            StudyId = "STUDY-001",
            SiteId = siteId,
            SiteName = "Compliant Site",
            CountryCode = "US",
            ReadinessPercentage = 100,
            FacetRegulatoryIrb = true,
            FacetCtaBudget = true,
            FacetEisfBaseline = true,
            FacetDoaLog = true,
            FacetStaffTraining = true,
            FacetIpRelease = true,
            CreatedBy = "test-admin",
            ReasonForChange = "Initial setup"
        };
    }

    [Fact]
    [GxPRequirement("PRD-SSU-001")]
    public void Certify_FromPendingCompliantGate_TransitionsToApprovedAndGeneratesToken()
    {
        // Arrange
        var gate = CreateCompliantGate();
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Pending);

        // Act
        gate.Certify("sponsor-user", Secret, TestTimestamp, "Approved for study startup");

        // Assert
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Approved);
        gate.GreenlightToken.Should().NotBeNullOrWhiteSpace();
        gate.GreenlightToken.Should().Contain(".");
        gate.GreenlightCertifiedBy.Should().Be("sponsor-user");
        gate.GreenlightCertifiedAt.Should().Be(TestTimestamp);
        gate.ReasonForChange.Should().Be("Approved for study startup");

        // Criterion 3: Successful certification records a SiteGreenlightApproved event in the aggregate event collection
        gate.DomainEvents.Should().ContainSingle();
        var domainEvent = gate.DomainEvents.Single().Should().BeOfType<SiteGreenlightApproved>().Subject;
        domainEvent.GateId.Should().Be(gate.Id);
        domainEvent.Study.Should().Be("STUDY-001");
        domainEvent.Site.Should().Be("SITE-001");
        domainEvent.CountryCode.Should().Be("US");
        domainEvent.CertifiedBy.Should().Be("sponsor-user");
        domainEvent.CertifiedAt.Should().Be(TestTimestamp);
        domainEvent.GreenlightToken.Should().Be(gate.GreenlightToken);
        domainEvent.IsOverridden.Should().BeFalse();
        domainEvent.Reason.Should().Be("Approved for study startup");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-002")]
    public void Certify_FromPendingOverriddenGate_TransitionsToApprovedEvenIfMissingFacets()
    {
        // Arrange: Missing facets but GxP override granted
        var gate = CreateCompliantGate();
        gate.FacetCtaBudget = false;
        gate.FacetIpRelease = false;
        gate.IsOverridden = true;
        gate.OverrideSponsorUserId = "sponsor-lead";
        gate.OverrideJustification = "Expedited activation";

        // Act
        gate.Certify("sponsor-user", Secret, TestTimestamp, "Overridden approval");

        // Assert
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Approved);
        gate.GreenlightToken.Should().NotBeNullOrWhiteSpace();
        gate.DomainEvents.Should().ContainSingle();
        var domainEvent = gate.DomainEvents.Single().Should().BeOfType<SiteGreenlightApproved>().Subject;
        domainEvent.IsOverridden.Should().BeTrue();
    }

    [Fact]
    public void Certify_FromPendingNonCompliantGate_ThrowsInvalidOperationException()
    {
        // Arrange
        var gate = CreateCompliantGate();
        gate.FacetIpRelease = false;
        gate.IsOverridden = false;

        // Act
        Action act = () => gate.Certify("sponsor-user", Secret, TestTimestamp);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not fulfill all 6 greenlight facets*");
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Pending);
        gate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Certify_FromRevokedGate_ThrowsDomainErrorAndBlocksTransition()
    {
        // Arrange: Gate in terminal state Revoked
        var gate = CreateCompliantGate(initialStatus: SiteGreenlightStatus.Revoked);

        // Act & Assert (Criterion 2: Calling certify on a gate with revoked status throws a domain error)
        Action act = () => gate.Certify("sponsor-user", Secret, TestTimestamp);
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*terminal state*");

        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Revoked);
        gate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Certify_FromApprovedGate_ThrowsDomainError()
    {
        // Arrange
        var gate = CreateCompliantGate();
        gate.Certify("sponsor-user", Secret, TestTimestamp);
        gate.ClearEvents();

        // Act & Assert: Cannot re-certify already approved gate
        Action act = () => gate.Certify("sponsor-user", Secret, TestTimestamp.AddDays(1));
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*current status is 'Approved'*");

        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Approved);
        gate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Certify_FromSuspendedGate_ThrowsDomainError()
    {
        // Arrange
        var gate = CreateCompliantGate(initialStatus: SiteGreenlightStatus.Suspended);

        // Act & Assert
        Action act = () => gate.Certify("sponsor-user", Secret, TestTimestamp);
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*current status is 'Suspended'*");

        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Suspended);
        gate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Revoke_FromApprovedGate_TransitionsToRevokedAndRecordsDomainEvent()
    {
        // Arrange
        var gate = CreateCompliantGate();
        gate.Certify("sponsor-user", Secret, TestTimestamp);
        gate.ClearEvents();

        // Act
        var revokeTime = TestTimestamp.AddDays(10);
        gate.Revoke("qa-officer", revokeTime, "Regulatory audit finding");

        // Assert
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Revoked);
        gate.GreenlightToken.Should().BeNull();
        gate.ReasonForChange.Should().Be("Regulatory audit finding");

        gate.DomainEvents.Should().ContainSingle();
        var domainEvent = gate.DomainEvents.Single().Should().BeOfType<SiteGreenlightRevoked>().Subject;
        domainEvent.GateId.Should().Be(gate.Id);
        domainEvent.Study.Should().Be("STUDY-001");
        domainEvent.Site.Should().Be("SITE-001");
        domainEvent.CountryCode.Should().Be("US");
        domainEvent.RevokedBy.Should().Be("qa-officer");
        domainEvent.RevokedAt.Should().Be(revokeTime);
        domainEvent.Reason.Should().Be("Regulatory audit finding");
    }

    [Fact]
    public void Revoke_FromPendingGate_TransitionsToRevokedAndRecordsDomainEvent()
    {
        // Arrange
        var gate = CreateCompliantGate();

        // Act
        gate.Revoke("qa-officer", TestTimestamp, "Site dropped before greenlight");

        // Assert
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Revoked);
        gate.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SiteGreenlightRevoked>();
    }

    [Fact]
    public void Revoke_FromRevokedGate_ThrowsDomainErrorBecauseTerminalState()
    {
        // Arrange
        var gate = CreateCompliantGate(initialStatus: SiteGreenlightStatus.Revoked);

        // Act & Assert
        Action act = () => gate.Revoke("qa-officer", TestTimestamp, "Duplicate revocation");
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*terminal state Revoked*");
    }

    [Fact]
    public void Suspend_FromApprovedGate_TransitionsToSuspendedAndRecordsDomainEvent()
    {
        // Arrange
        var gate = CreateCompliantGate();
        gate.Certify("sponsor-user", Secret, TestTimestamp);
        gate.ClearEvents();

        // Act
        var suspendTime = TestTimestamp.AddDays(5);
        gate.Suspend("safety-officer", suspendTime, "Temporary hold for inspection");

        // Assert
        gate.OverallStatus.Should().Be(SiteGreenlightStatus.Suspended);
        gate.ReasonForChange.Should().Be("Temporary hold for inspection");

        gate.DomainEvents.Should().ContainSingle();
        var domainEvent = gate.DomainEvents.Single().Should().BeOfType<SiteGreenlightSuspended>().Subject;
        domainEvent.GateId.Should().Be(gate.Id);
        domainEvent.SuspendedBy.Should().Be("safety-officer");
        domainEvent.SuspendedAt.Should().Be(suspendTime);
        domainEvent.Reason.Should().Be("Temporary hold for inspection");
    }

    [Fact]
    public void Suspend_FromRevokedGate_ThrowsDomainErrorBecauseTerminalState()
    {
        // Arrange
        var gate = CreateCompliantGate(initialStatus: SiteGreenlightStatus.Revoked);

        // Act & Assert
        Action act = () => gate.Suspend("safety-officer", TestTimestamp, "Cannot suspend revoked");
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*terminal state Revoked*");
    }

    [Fact]
    public void Suspend_FromSuspendedGate_ThrowsDomainError()
    {
        // Arrange
        var gate = CreateCompliantGate(initialStatus: SiteGreenlightStatus.Suspended);

        // Act & Assert
        Action act = () => gate.Suspend("safety-officer", TestTimestamp, "Already suspended");
        act.Should().Throw<SiteGreenlightTransitionException>()
            .WithMessage("*already suspended*");
    }

    [Fact]
    public async Task EntityFramework_MapsStronglyTypedStatusEnum_ToDatabaseStringColumns()
    {
        // Criterion 4: Entity Framework maps the strongly typed status enum to database string columns without schema breakage
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var db = new CtmsDbContext(options))
        {
            var gate1 = CreateCompliantGate("SITE-PENDING", SiteGreenlightStatus.Pending);
            var gate2 = CreateCompliantGate("SITE-REVOKED", SiteGreenlightStatus.Revoked);
            var gate3 = CreateCompliantGate("SITE-APPROVED", SiteGreenlightStatus.Pending);
            gate3.Certify("sponsor", Secret, TestTimestamp);

            db.CtmsSiteGreenlightGates.AddRange(gate1, gate2, gate3);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = new CtmsDbContext(options))
        {
            var retrieved1 = await db.CtmsSiteGreenlightGates.SingleAsync(g => g.SiteId == "SITE-PENDING", TestContext.Current.CancellationToken);
            retrieved1.OverallStatus.Should().Be(SiteGreenlightStatus.Pending);

            var retrieved2 = await db.CtmsSiteGreenlightGates.SingleAsync(g => g.SiteId == "SITE-REVOKED", TestContext.Current.CancellationToken);
            retrieved2.OverallStatus.Should().Be(SiteGreenlightStatus.Revoked);

            var retrieved3 = await db.CtmsSiteGreenlightGates.SingleAsync(g => g.SiteId == "SITE-APPROVED", TestContext.Current.CancellationToken);
            retrieved3.OverallStatus.Should().Be(SiteGreenlightStatus.Approved);
        }
    }
}
