using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Testing;
using Cadence.Testing.GoldenMaster;
using Cadence.Testing.Security;
using Cadence.UnitTests.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.GoldenMaster;

/// <summary>
/// `MIG-SUBJ-025` coverage for the golden-master test harness's fixture loader and sentinel-aware assert
/// primitives -- per this ticket's own Verification &amp; Test Requirements, the fail-fast-on-mismatched-pair
/// test is the highest-value case here (it protects every downstream fixture-replay test from silently
/// under-running), and the sentinel-matching tests use hand-constructed <see cref="JsonElement"/> fixtures,
/// independent of the real fixture files (those are exercised separately, for their scenario counts only, in
/// this class).
/// </summary>
public sealed class GoldenMasterFixtureLoaderTests
{
    [Fact]
    public void LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds()
    {
        // The ticket text (03_tickets.md AC #3) describes this pair as "17 SCN-* scenario IDs"; the real
        // fixture file (verified directly: `jq '.scenarios | length' input_valid_nominal.json` => 19) actually
        // declares 19 (SCN-001-A/B, 002-A/B, 003-A, 004-A, 005-A/B/C/D, 006-A, 007-A, 008-A/B, 009-A/B,
        // 010-A/B/C). This test asserts against the real, verified file content, not the ticket's stale count.
        IReadOnlyList<GoldenMasterScenario> scenarios = GoldenMasterFixtureLoader.LoadScenarios(
            "docs/migration/slices/execution-subjects/fixtures/input_valid_nominal.json",
            "docs/migration/slices/execution-subjects/fixtures/output_expected_nominal.json");

        scenarios.Should().HaveCount(19);
        scenarios.Select(s => s.ScenarioId).Should().OnlyHaveUniqueItems();
        scenarios.Should().Contain(s => s.ScenarioId == "SCN-001-A");
        scenarios.Should().Contain(s => s.ScenarioId == "SCN-010-C");
    }

    [Fact]
    public void LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds()
    {
        // Per the design's file-pairing (02_technical_design.md §6.1), edge cases pair against the SAME
        // output_expected_nominal.json the nominal-input pair above also uses -- scenario_ids never collide
        // between the two input files (SCN-* vs EDGE-*), so the loader's join is unambiguous either way.
        IReadOnlyList<GoldenMasterScenario> scenarios = GoldenMasterFixtureLoader.LoadScenarios(
            "docs/migration/slices/execution-subjects/fixtures/input_valid_edge_cases.json",
            "docs/migration/slices/execution-subjects/fixtures/output_expected_nominal.json");

        scenarios.Should().HaveCount(11);
        scenarios.Select(s => s.ScenarioId).Should().OnlyHaveUniqueItems();
        scenarios.Should().Contain(s => s.ScenarioId == "EDGE-001");
        scenarios.Should().Contain(s => s.ScenarioId == "EDGE-010");
    }

    [Fact]
    public void LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds()
    {
        IReadOnlyList<GoldenMasterScenario> scenarios = GoldenMasterFixtureLoader.LoadScenarios(
            "docs/migration/slices/execution-subjects/fixtures/input_invalid_validation_errors.json",
            "docs/migration/slices/execution-subjects/fixtures/output_expected_errors.json");

        scenarios.Should().HaveCount(21);
        scenarios.Select(s => s.ScenarioId).Should().OnlyHaveUniqueItems();

        // INV-020's expected body carries the same fixed "detail" string as INV-008 -- spot-check that the
        // loader actually surfaces ExpectedDetail (extracted from body.detail) rather than leaving it null.
        GoldenMasterScenario inv020 = scenarios.Single(s => s.ScenarioId == "INV-020");
        inv020.ExpectedStatus.Should().Be(400);
        inv020.ExpectedDetail.Should().Be("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public void LoadScenarios_PreservesInputFileOrderForMultiStepScenarios()
    {
        // SCN-001-B's enrollment_index assertion (1) is only meaningful if SCN-001-A (which claims index 0)
        // is replayed first against the same shared database -- the loader must preserve fixture order, not
        // an incidental dictionary/hash order, for this stateful replay to make sense at all.
        IReadOnlyList<GoldenMasterScenario> scenarios = GoldenMasterFixtureLoader.LoadScenarios(
            "docs/migration/slices/execution-subjects/fixtures/input_valid_nominal.json",
            "docs/migration/slices/execution-subjects/fixtures/output_expected_nominal.json");

        int indexOfA = scenarios.ToList().FindIndex(s => s.ScenarioId == "SCN-001-A");
        int indexOfB = scenarios.ToList().FindIndex(s => s.ScenarioId == "SCN-001-B");

        indexOfA.Should().BeLessThan(indexOfB);
    }

    [Fact]
    public void LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario()
    {
        // Deliberately mismatched, test-only fixture pair (not a change to the real fixtures): the input side
        // declares two scenario_ids ("ORPHAN-INPUT-ONLY-1"/"-2") the output side never pairs -- the
        // highest-value case this ticket's Verification section calls out. Both orphans are on the INPUT
        // side deliberately: see GoldenMasterFixtureLoader's own remarks for why an output-only extra (the
        // real fixtures' own nominal+edge-cases shared-output-file convention) must NOT also throw.
        string inputPath = WriteTempFixture(new
        {
            scenarios = new object[]
            {
                new { scenario_id = "SHARED-001", kind = "http", method = "GET", route = "/x" },
                new { scenario_id = "ORPHAN-INPUT-ONLY-1", kind = "http", method = "GET", route = "/y" },
                new { scenario_id = "ORPHAN-INPUT-ONLY-2", kind = "http", method = "GET", route = "/w" },
            },
        });
        string outputPath = WriteTempFixture(new
        {
            results = new object[]
            {
                new { scenario_id = "SHARED-001", status = 200, body = new { } },
            },
        });

        Action act = () => GoldenMasterFixtureLoader.LoadScenarios(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ORPHAN-INPUT-ONLY-1*")
            .Which.Message.Should().Contain("ORPHAN-INPUT-ONLY-2");
    }

    [Fact]
    public void LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt()
    {
        // A narrower variant of the fail-fast case above: exactly one orphan, only on the input side, with an
        // otherwise-complete pair -- confirms the loader does not simply "return what it can join" and move on.
        string inputPath = WriteTempFixture(new
        {
            scenarios = new object[]
            {
                new { scenario_id = "ONLY-IN-INPUT", kind = "http", method = "GET", route = "/z" },
            },
        });
        string outputPath = WriteTempFixture(new { results = Array.Empty<object>() });

        Action act = () => GoldenMasterFixtureLoader.LoadScenarios(inputPath, outputPath);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ONLY-IN-INPUT*");
    }

    [Fact]
    public void LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow()
    {
        // Locks in the asymmetric behavior GoldenMasterFixtureLoader's own remarks document: an output-only
        // extra is legitimate (this is exactly how the real output_expected_nominal.json -- one shared file --
        // pairs against both input_valid_nominal.json and input_valid_edge_cases.json without either real
        // pairing throwing), so it must not be treated as a fixture-integrity error. Only the entry the input
        // file actually declares is returned; the output-only extra is simply not replayable and is dropped.
        string inputPath = WriteTempFixture(new
        {
            scenarios = new object[]
            {
                new { scenario_id = "SHARED-001", kind = "http", method = "GET", route = "/x" },
            },
        });
        string outputPath = WriteTempFixture(new
        {
            results = new object[]
            {
                new { scenario_id = "SHARED-001", status = 200, body = new { } },
                new { scenario_id = "OUTPUT-ONLY-EXTRA", status = 200, body = new { } },
            },
        });

        IReadOnlyList<GoldenMasterScenario> scenarios = GoldenMasterFixtureLoader.LoadScenarios(inputPath, outputPath);

        scenarios.Should().ContainSingle().Which.ScenarioId.Should().Be("SHARED-001");
    }

    private static string WriteTempFixture(object content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"golden-master-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(content));
        return path;
    }
}

/// <summary>
/// Sentinel-matching coverage for <see cref="GoldenMasterAssert.BodyMatches"/> against hand-constructed
/// <see cref="JsonElement"/> fixtures, independent of the real fixture files, per this ticket's own
/// Verification &amp; Test Requirements.
/// </summary>
public sealed class GoldenMasterAssertBodyMatchesTests
{
    [Fact]
    public void BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes()
    {
        JsonElement expected = ToJsonElement(new { id = "<opaque>" });
        JsonElement actual = ToJsonElement(new { id = "11111111-1111-1111-1111-111111111111" });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().NotThrow();
    }

    [Fact]
    public void BodyMatches_GivenOpaqueSentinelAndNullActual_Fails()
    {
        // Ticket AC: "given an expected-body field value of \"<opaque>\" ... given the actual field is null,
        // then it fails." Verified against the real fixtures (output_expected_*.json): plain "<opaque>" is
        // used exclusively for `id` fields, which are never legitimately null in a successful response, so no
        // real fixture usage contradicts encoding this as a hard non-null requirement.
        JsonElement expected = ToJsonElement(new { id = "<opaque>" });
        JsonElement actual = ToJsonElement(new { id = (string?)null });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().Throw<GoldenMasterAssertionException>().WithMessage("*id*");
    }

    [Fact]
    public void BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails()
    {
        // Boundary check (ticket AC): does plain "<opaque>" ever legitimately allow null in the real fixtures?
        // No -- confirmed by inspection of every "<opaque>"/"<opaque-non-null>" usage in
        // fixtures/output_expected_*.json. Both sentinels therefore reject null identically here; this test
        // locks in "<opaque-non-null>"'s half of that behavior specifically.
        JsonElement expected = ToJsonElement(new { encrypted_demographics = "<opaque-non-null>" });
        JsonElement actual = ToJsonElement(new { encrypted_demographics = (string?)null });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().Throw<GoldenMasterAssertionException>().WithMessage("*encrypted_demographics*");
    }

    [Fact]
    public void BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes()
    {
        JsonElement expected = ToJsonElement(new { encrypted_demographics = "<opaque-non-null>" });
        JsonElement actual = ToJsonElement(new { encrypted_demographics = "gAAAAA...ciphertext" });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().NotThrow();
    }

    [Fact]
    public void BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes()
    {
        JsonElement expected = ToJsonElement(new { study_id = "<unchanged>" });
        JsonElement preState = ToJsonElement(new { study_id = "STUDY-001" });
        JsonElement actual = ToJsonElement(new { study_id = "STUDY-001" });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected, preState);

        act.Should().NotThrow();
    }

    [Fact]
    public void BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails()
    {
        JsonElement expected = ToJsonElement(new { study_id = "<unchanged>" });
        JsonElement preState = ToJsonElement(new { study_id = "STUDY-001" });
        JsonElement actual = ToJsonElement(new { study_id = "STUDY-DIFFERENT" });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected, preState);

        act.Should().Throw<GoldenMasterAssertionException>().WithMessage("*study_id*");
    }

    [Fact]
    public void BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy()
    {
        // The design's own wording: "<unchanged> ... requires the harness to have captured a pre-state
        // snapshot" -- omitting it must not silently pass.
        JsonElement expected = ToJsonElement(new { study_id = "<unchanged>" });
        JsonElement actual = ToJsonElement(new { study_id = "STUDY-001" });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().Throw<GoldenMasterAssertionException>().WithMessage("*pre-state*");
    }

    [Fact]
    public void BodyMatches_GivenLiteralExpectedValue_ComparesDirectly()
    {
        JsonElement expectedMatching = ToJsonElement(new { status = "SCREENING" });
        JsonElement actualMatching = ToJsonElement(new { status = "SCREENING" });
        JsonElement actualMismatched = ToJsonElement(new { status = "ENROLLED" });

        Action actMatching = () => GoldenMasterAssert.BodyMatches(actualMatching, expectedMatching);
        actMatching.Should().NotThrow();

        Action actMismatched = () => GoldenMasterAssert.BodyMatches(actualMismatched, expectedMatching);
        actMismatched.Should().Throw<GoldenMasterAssertionException>().WithMessage("*status*");
    }

    [Fact]
    public void BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields()
    {
        JsonElement expected = ToJsonElement(new
        {
            id = "<opaque>",
            missing_documents = new[] { "A", "B" },
            nested = new { study_id = "<unchanged>" },
        });
        JsonElement preState = ToJsonElement(new { nested = new { study_id = "STUDY-001" } });
        JsonElement actual = ToJsonElement(new
        {
            id = "abc-123",
            missing_documents = new[] { "A", "B" },
            nested = new { study_id = "STUDY-001" },
            extra_field_not_asserted_by_the_fixture = "ignored",
        });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected, preState);

        act.Should().NotThrow();
    }

    [Fact]
    public void BodyMatches_GivenMismatchedArrayLength_Fails()
    {
        JsonElement expected = ToJsonElement(new { missing_documents = new[] { "A", "B" } });
        JsonElement actual = ToJsonElement(new { missing_documents = new[] { "A" } });

        Action act = () => GoldenMasterAssert.BodyMatches(actual, expected);

        act.Should().Throw<GoldenMasterAssertionException>().WithMessage("*missing_documents*");
    }

    private static JsonElement ToJsonElement(object value) => JsonSerializer.SerializeToElement(value);
}

/// <summary>
/// AC #7 coverage: <see cref="GoldenMasterAssert.DbStateMatches{TDbContext}"/>/
/// <see cref="GoldenMasterAssert.AuditLogContains{TDbContext}"/>, exercised end to end against a real,
/// DI-resolved <see cref="ExecutionDbContext"/> via <see cref="ExecutionApiFactory"/> -- proving both the
/// generic harness primitives AND this ticket's own <see cref="ExecutionApiFactory"/> Target File actually
/// work together, not merely that they compile.
/// </summary>
public sealed class GoldenMasterAssertDbAndAuditTests : IDisposable
{
    private readonly ExecutionApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail()
    {
        using var client = _factory.CreateSignedClient(); // forces host/DbContext construction, per MIG-SUBJ-024 precedent.
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            dbContext.ClinicalSubjects.Add(ClinicalSubject.Create(
                new SubjectCode("SUBJ-DBSTATE-TEST"), new StudyCode("STUDY-DBSTATE-TEST"), null, 0, null, null));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var scenario = new GoldenMasterScenario
        {
            ScenarioId = "TEST-DB-MISMATCH",
            Kind = "http",
            DbAssertions = JsonSerializer.SerializeToElement(new { enrollment_index = 1 }),
        };

        Func<Task> act = () => GoldenMasterAssert.DbStateMatches<ExecutionDbContext>(
            _factory.Services,
            scenario,
            async (db, ct) =>
            {
                ClinicalSubject subject = await db.ClinicalSubjects.AsNoTracking().SingleAsync(ct);
                return JsonSerializer.SerializeToElement(new { enrollment_index = subject.EnrollmentIndex });
            },
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GoldenMasterAssertionException>())
            .Which.Message.Should().Contain("TEST-DB-MISMATCH").And.Contain("enrollment_index");
    }

    [Fact]
    public async Task DbStateMatches_GivenAssertionThatHolds_DoesNotThrow()
    {
        using var client = _factory.CreateSignedClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            dbContext.ClinicalSubjects.Add(ClinicalSubject.Create(
                new SubjectCode("SUBJ-DBSTATE-OK"), new StudyCode("STUDY-DBSTATE-OK"), null, 3, null, null));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var scenario = new GoldenMasterScenario
        {
            ScenarioId = "TEST-DB-MATCH",
            Kind = "http",
            DbAssertions = JsonSerializer.SerializeToElement(new { enrollment_index = 3 }),
        };

        Func<Task> act = () => GoldenMasterAssert.DbStateMatches<ExecutionDbContext>(
            _factory.Services,
            scenario,
            async (db, ct) =>
            {
                ClinicalSubject subject = await db.ClinicalSubjects.AsNoTracking().SingleAsync(ct);
                return JsonSerializer.SerializeToElement(new { enrollment_index = subject.EnrollmentIndex });
            },
            TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries()
    {
        using var client = _factory.CreateSignedClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                EntityName = "clinical_subjects",
                EntityId = Guid.NewGuid().ToString(),
                Action = "ENROLL_SUBJECT",
                UserId = "tester",
            });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var scenario = new GoldenMasterScenario
        {
            ScenarioId = "TEST-AUDIT-MISMATCH",
            Kind = "http",
            AuditAssertions = JsonSerializer.SerializeToElement(new[]
            {
                new { table_name = "clinical_subjects", action = "BLOCKED_ENROLLMENT" },
            }),
        };

        Func<Task> act = () => GoldenMasterAssert.AuditLogContains<ExecutionDbContext>(
            _factory.Services, scenario, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GoldenMasterAssertionException>())
            .Which.Message.Should().Contain("TEST-AUDIT-MISMATCH").And.Contain("BLOCKED_ENROLLMENT").And.Contain("ENROLL_SUBJECT");
    }

    [Fact]
    public async Task AuditLogContains_GivenAMatchingEntry_DoesNotThrow()
    {
        using var client = _factory.CreateSignedClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                EntityName = "clinical_subjects",
                EntityId = Guid.NewGuid().ToString(),
                Action = "ENROLL_SUBJECT",
                UserId = "tester",
            });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var scenario = new GoldenMasterScenario
        {
            ScenarioId = "TEST-AUDIT-MATCH",
            Kind = "http",
            AuditAssertions = JsonSerializer.SerializeToElement(new[]
            {
                new { table_name = "clinical_subjects", action = "ENROLL_SUBJECT" },
            }),
        };

        Func<Task> act = () => GoldenMasterAssert.AuditLogContains<ExecutionDbContext>(
            _factory.Services, scenario, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
