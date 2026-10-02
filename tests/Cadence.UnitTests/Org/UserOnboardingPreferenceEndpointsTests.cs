using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Org.Endpoints;
using Cadence.Apps.Org.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Org;

public sealed class UserOnboardingPreferenceEndpointsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    public UserOnboardingPreferenceEndpointsTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OrgDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<OrgDbContext>().UseSqlite(_connection).Options);

    private static ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[] { new Claim(CadenceClaimTypes.Subject, userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task GetUserOnboardingPreference_WhenNoPreferenceExists_ReturnsDefaultWithUnixEpoch()
    {
        using var dbContext = CreateContext();
        var user = CreateUser("user-123");

        var result = await OnboardingPreferencesEndpoints.GetUserOnboardingPreferenceAsync(
            "super_admin", user, dbContext, TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<OnboardingPreferencesEndpoints.UserOnboardingPreferenceDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Persona.Should().Be("super_admin");
        okResult.Value.CurrentStep.Should().Be(1);
        okResult.Value.IsActive.Should().BeTrue();
        okResult.Value.Disabled.Should().BeFalse();
        okResult.Value.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task SaveUserOnboardingPreference_WhenNew_CreatesRecord()
    {
        using var dbContext = CreateContext();
        var user = CreateUser("user-456");
        var updatedAt = _clock.UtcNow;

        var request = new OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceRequest(
            Persona: "sponsor_designer",
            CurrentStep: 3,
            IsActive: true,
            Disabled: false,
            EventsJson: "[{\"type\":\"step_completed\"}]",
            UpdatedAt: updatedAt
        );

        var result = await OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceAsync(
            request, user, dbContext, _clock, TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<OnboardingPreferencesEndpoints.UserOnboardingPreferenceDto>>().Subject;
        okResult.Value!.Persona.Should().Be("sponsor_designer");
        okResult.Value.CurrentStep.Should().Be(3);
        okResult.Value.UpdatedAt.Should().Be(updatedAt);

        var saved = await dbContext.UserOnboardingPreferences.FirstOrDefaultAsync(
            p => p.UserId == "user-456" && p.Persona == "sponsor_designer",
            TestContext.Current.CancellationToken);
        saved.Should().NotBeNull();
        saved!.CurrentStep.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task SaveUserOnboardingPreference_WhenNewerUpdate_UpdatesRecord()
    {
        using var dbContext = CreateContext();
        var user = CreateUser("user-789");
        var initialTime = _clock.UtcNow;

        var req1 = new OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceRequest(
            Persona: "site_crc",
            CurrentStep: 2,
            IsActive: true,
            Disabled: false,
            EventsJson: "[]",
            UpdatedAt: initialTime
        );
        await OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceAsync(req1, user, dbContext, _clock, TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var newerTime = _clock.UtcNow;

        var req2 = new OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceRequest(
            Persona: "site_crc",
            CurrentStep: 4,
            IsActive: false,
            Disabled: true,
            EventsJson: "[{\"type\":\"tour_disabled\"}]",
            UpdatedAt: newerTime
        );
        var result = await OnboardingPreferencesEndpoints.SaveUserOnboardingPreferenceAsync(req2, user, dbContext, _clock, TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<OnboardingPreferencesEndpoints.UserOnboardingPreferenceDto>>().Subject;
        okResult.Value!.CurrentStep.Should().Be(4);
        okResult.Value.Disabled.Should().BeTrue();
        okResult.Value.IsActive.Should().BeFalse();
        okResult.Value.UpdatedAt.Should().Be(newerTime);
    }
}
