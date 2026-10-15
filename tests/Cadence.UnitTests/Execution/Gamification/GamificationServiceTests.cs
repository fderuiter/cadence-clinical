using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Gamification;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Gamification;

public class GamificationServiceTests
{
    private static ExecutionDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ExecutionDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetProgressAsync_WhenNoRecord_ReturnsDefaultProgress()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        var service = new GamificationService(dbContext, clock, publishEndpoint);

        var result = await service.GetProgressAsync("SUBJ-100", "STUDY-001", ct);

        result.Should().NotBeNull();
        result.SubjectId.Should().Be("SUBJ-100");
        result.StudyId.Should().Be("STUDY-001");
        result.CurrentStreak.Should().Be(0);
        result.LongestStreak.Should().Be(0);
        result.TotalBonusEarned.Should().Be(0m);
        result.MilestonesAchieved.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessSubmissionAsync_FirstSubmission_InitializesStreakToOne()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new GamificationService(dbContext, clock, publishEndpoint);

        var deviceTime = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);
        var request = new ProcessGamificationSubmissionRequest(
            SubjectId: "SUBJ-100",
            StudyId: "STUDY-001",
            InstrumentId: "FORM-1",
            SequenceNumber: 1,
            DeviceTimestamp: deviceTime,
            IsOnTime: true);

        var response = await service.ProcessSubmissionAsync(request, ct);

        response.SubjectId.Should().Be("SUBJ-100");
        response.CurrentStreak.Should().Be(1);
        response.LongestStreak.Should().Be(1);
        response.TotalSubmissions.Should().Be(1);
        response.TotalBonusEarned.Should().Be(0m);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessSubmissionAsync_ThreeConsecutiveDays_TriggersStreak3DayMilestone()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new GamificationService(dbContext, clock, publishEndpoint);

        var day1 = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        var day3 = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

        await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-1", 1, day1, true),
            ct);
        await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-2", 2, day2, true),
            ct);

        var resultDay3 = await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-3", 3, day3, true),
            ct);

        resultDay3.CurrentStreak.Should().Be(3);
        resultDay3.LongestStreak.Should().Be(3);
        resultDay3.TotalBonusEarned.Should().Be(0m); // Recognition milestone
        resultDay3.MilestonesAchieved.Should().Contain("STREAK_3_DAY");

        await publishEndpoint.Received(1).Publish(
            Arg.Is<ParticipantStreakMilestoneAchieved>(m =>
                m.SubjectId == "SUBJ-100" &&
                m.StudyId == "STUDY-001" &&
                m.MilestoneType == "STREAK_3_DAY" &&
                m.StreakCount == 3 &&
                m.BonusAmount == 0m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessSubmissionAsync_SevenConsecutiveDays_Awards25DollarStipendBonus()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new GamificationService(dbContext, clock, publishEndpoint);

        for (int day = 1; day <= 7; day++)
        {
            var date = new DateTimeOffset(2026, 10, day, 9, 0, 0, TimeSpan.Zero);
            var result = await service.ProcessSubmissionAsync(
                new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", $"FORM-{day}", day, date, true),
                ct);

            if (day == 7)
            {
                result.CurrentStreak.Should().Be(7);
                result.TotalBonusEarned.Should().Be(25.00m);
                result.MilestonesAchieved.Should().Contain("STREAK_7_DAY");

                await publishEndpoint.Received(1).Publish(
                    Arg.Is<ParticipantStreakMilestoneAchieved>(m =>
                        m.SubjectId == "SUBJ-100" &&
                        m.StudyId == "STUDY-001" &&
                        m.MilestoneType == "STREAK_7_DAY" &&
                        m.StreakCount == 7 &&
                        m.BonusAmount == 25.00m),
                    Arg.Any<CancellationToken>());
            }
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessSubmissionAsync_SameDay_DoesNotIncrementStreak()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new GamificationService(dbContext, clock, publishEndpoint);

        var submission1 = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var submission2 = new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

        await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-1", 1, submission1, true),
            ct);
        var result2 = await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-2", 2, submission2, true),
            ct);

        result2.CurrentStreak.Should().Be(1);
        result2.TotalSubmissions.Should().Be(2);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessSubmissionAsync_MissedDay_ResetsStreakAndPreservesLongestStreak()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var dbContext = CreateDbContext(connection);

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new GamificationService(dbContext, clock, publishEndpoint);

        var day1 = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        var day4 = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero); // Day 3 missed

        await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-1", 1, day1, true),
            ct);
        await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-2", 2, day2, true),
            ct);

        var resultDay4 = await service.ProcessSubmissionAsync(
            new ProcessGamificationSubmissionRequest("SUBJ-100", "STUDY-001", "FORM-3", 3, day4, true),
            ct);

        resultDay4.CurrentStreak.Should().Be(1);
        resultDay4.LongestStreak.Should().Be(2);
    }
}
