using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Coding;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Coding;

public sealed class SynonymImportServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ExecutionDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly SynonymImportService _service;

    public SynonymImportServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ExecutionDbContext(options);
        _dbContext.Database.EnsureCreated();

        _clock = new FakeClock();
        _service = new SynonymImportService(_dbContext, _clock);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ValidatePreflightAsync_WithValidCsv_ReturnsExpectedMetrics()
    {
        var csvBytes = Encoding.UTF8.GetBytes("""
            verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level
            headache,MEDDRA,26.0,10019211,Headache,PT
            headache,MEDDRA,26.0,10019211,Headache,PT
            fever,MEDDRA,26.0,10037660,Pyrexia,PT
            """);

        var report = await _service.ValidatePreflightAsync(
            csvBytes,
            "test_rules.csv",
            "STUDY",
            "STUDY-101",
            TestContext.Current.CancellationToken);

        report.TotalRows.Should().Be(3);
        report.ValidCount.Should().Be(2);
        report.DuplicateCount.Should().Be(1);
        report.InvalidCount.Should().Be(1);
        report.CanImport.Should().BeTrue();
        report.InvalidRows.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidatePreflightAsync_WithInvalidRows_ReturnsRowErrors()
    {
        var csvBytes = Encoding.UTF8.GetBytes("""
            verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level
            ,MEDDRA,26.0,10019211,Headache,PT
            nausea,INVALID_DICT,26.0,10028813,Nausea,PT
            chills,MEDDRA,26.0,,Chills,PT
            """);

        var report = await _service.ValidatePreflightAsync(
            csvBytes,
            "invalid_rules.csv",
            "ENTERPRISE",
            null,
            TestContext.Current.CancellationToken);

        report.TotalRows.Should().Be(3);
        report.ValidCount.Should().Be(0);
        report.InvalidCount.Should().Be(3);
        report.CanImport.Should().BeFalse();
        report.InvalidRows.Should().HaveCount(3);
        report.InvalidRows.Select(e => e.RowNumber).Should().BeEquivalentTo((System.Collections.Generic.IEnumerable<int>)new[] { 2, 3, 4 });
    }

    [Fact]
    public async Task ImportSynonymRulesAsync_WithValidCsv_PersistsRulesAndJobLedger()
    {
        var csvBytes = Encoding.UTF8.GetBytes("""
            verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level
            headache,MEDDRA,26.0,10019211,Headache,PT
            dizziness,MEDDRA,26.0,10013573,Dizziness,PT
            """);

        var result = await _service.ImportSynonymRulesAsync(
            csvBytes,
            "meddra_synonyms.csv",
            "STUDY",
            "STUDY-202",
            "Initial MedDRA study synonym ingestion",
            "dm_user_01",
            TestContext.Current.CancellationToken);

        result.ImportedRules.Should().HaveCount(2);
        result.Job.Should().NotBeNull();
        result.Job.Id.Should().NotBeEmpty();

        var rules = await _dbContext.SynonymRules.ToListAsync(TestContext.Current.CancellationToken);
        rules.Should().HaveCount(2);
        rules.Select(r => r.VerbatimText).Should().BeEquivalentTo("headache", "dizziness");
        rules.All(r => r.Scope == "STUDY" && r.StudyId == "STUDY-202").Should().BeTrue();

        var jobs = await _dbContext.BulkSynonymImportJobs.ToListAsync(TestContext.Current.CancellationToken);
        jobs.Should().HaveCount(1);
        var job = jobs.Single();
        job.FileName.Should().Be("meddra_synonyms.csv");
        job.FileHash.Should().NotBeNullOrWhiteSpace();
        job.TotalRows.Should().Be(2);
        job.ValidRows.Should().Be(2);
    }

    [Fact]
    public async Task GetActiveSynonymRulesAsync_ReturnsStudyAndEnterpriseActiveRules()
    {
        var importJobId = Guid.NewGuid();
        var rule1 = new SynonymRule(
            Guid.NewGuid(),
            "STUDY",
            "STUDY-301",
            "HEADACHE",
            "MEDDRA",
            "26.0",
            "10019211",
            "Headache",
            "PT",
            importJobId,
            "dm_user",
            "Initial import",
            _clock.UtcNow.UtcDateTime);

        var rule2 = new SynonymRule(
            Guid.NewGuid(),
            "ENTERPRISE",
            null,
            "FATIGUE",
            "MEDDRA",
            "26.0",
            "10016256",
            "Fatigue",
            "PT",
            importJobId,
            "dm_user",
            "Initial import",
            _clock.UtcNow.UtcDateTime);

        var rule3 = new SynonymRule(
            Guid.NewGuid(),
            "STUDY",
            "STUDY-999",
            "OTHER_STUDY_VERBATIM",
            "MEDDRA",
            "26.0",
            "10000000",
            "Other",
            "PT",
            importJobId,
            "dm_user",
            "Initial import",
            _clock.UtcNow.UtcDateTime);

        var inactiveRule = new SynonymRule(
            Guid.NewGuid(),
            "STUDY",
            "STUDY-301",
            "INACTIVE_VERBATIM",
            "MEDDRA",
            "26.0",
            "10000001",
            "Inactive",
            "PT",
            importJobId,
            "dm_user",
            "Initial import",
            _clock.UtcNow.UtcDateTime);
        inactiveRule.Deactivate("Deprecation");

        _dbContext.SynonymRules.AddRange(rule1, rule2, rule3, inactiveRule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var activeRules = await _service.GetActiveSynonymRulesAsync("STUDY-301", TestContext.Current.CancellationToken);

        activeRules.Should().HaveCount(2);
        activeRules.Select(r => r.VerbatimText).Should().BeEquivalentTo("HEADACHE", "FATIGUE");
    }

    [Fact]
    public async Task DeactivateRuleAsync_DeactivatesRuleSuccessfully()
    {
        var rule = new SynonymRule(
            Guid.NewGuid(),
            "STUDY",
            "STUDY-404",
            "MIGRAINE",
            "MEDDRA",
            "26.0",
            "10027599",
            "Migraine",
            "PT",
            Guid.NewGuid(),
            "dm_user",
            "Initial import",
            _clock.UtcNow.UtcDateTime);

        _dbContext.SynonymRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var success = await _service.DeactivateRuleAsync(
            rule.Id,
            "Outdated synonym mapping removed by Data Manager",
            TestContext.Current.CancellationToken);

        success.Should().BeTrue();

        var updatedRule = await _dbContext.SynonymRules.FindAsync(new object[] { rule.Id }, TestContext.Current.CancellationToken);
        updatedRule.Should().NotBeNull();
        updatedRule!.Status.Should().Be("INACTIVE");
    }
}
