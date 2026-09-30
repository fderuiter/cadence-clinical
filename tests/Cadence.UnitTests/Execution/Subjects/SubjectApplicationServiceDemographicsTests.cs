using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// `MIG-SUBJ-019` coverage for <see cref="SubjectApplicationService.UpdateDemographicsAsync"/> and
/// <see cref="SubjectApplicationService.DeleteDemographicsAsync"/>: SCN-004's full pre/post-randomization
/// lock matrix (strat factors and demographics, both gating on <see cref="ClinicalSubject.IsPostRandomization"/>
/// but raising distinct error codes for demographics-lock vs. soft-delete), SCN-006's pre-randomization
/// success path, and the raise site #22 regression test for the corrupted-existing-ciphertext-must-fail-loudly
/// fix (not the legacy's spurious LOCKED_FACTOR_MUTATION).
/// </summary>
public class SubjectApplicationServiceDemographicsTests
{
    private const string CorruptedCiphertext = FakeDemographicsEncryptionService.CorruptedCiphertext;

    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public FakeDemographicsEncryptionService Encryption { get; } = new();

        public RecordingAuditLogWriter AuditLog { get; } = new();

        public FakeCurrentAuditContextAccessor AuditContext { get; } = new();

        public SubjectApplicationService Service { get; }

        public Harness() =>
            Service = new SubjectApplicationService(Subjects, Consents, Compliance, Encryption, AuditLog, AuditContext);
    }

    private static readonly StudyCode Study = new("STUDY-001");

    private static async Task<ClinicalSubject> SeedSubjectAsync(InMemorySubjectRepository subjects, string subjectCode,
        string? encryptedDemographics = null, IReadOnlyDictionary<string, object?>? stratFactors = null,
        SubjectState status = SubjectState.Screening)
    {
        var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), Study, siteCode: null,
            enrollmentIndex: 0, encryptedDemographics, stratFactors);
        AdvanceToStatus(subject, status);

        await subjects.AddAsync(subject, CancellationToken.None);
        return subject;
    }

    /// <summary>
    /// Walks <paramref name="subject"/> through the legal <see cref="SubjectTransitionGraph"/> step chain
    /// needed to reach <paramref name="status"/> from <see cref="SubjectState.Screening"/> (e.g.
    /// <see cref="SubjectState.Active"/> requires Screening -&gt; Enrolled -&gt; Randomized -&gt; Active --
    /// there is no single-step transition directly into a post-randomization state).
    /// </summary>
    private static void AdvanceToStatus(ClinicalSubject subject, SubjectState status)
    {
        SubjectState[] path = status switch
        {
            SubjectState.Screening => [],
            SubjectState.Enrolled => [SubjectState.Enrolled],
            SubjectState.Randomized => [SubjectState.Enrolled, SubjectState.Randomized],
            SubjectState.Active => [SubjectState.Enrolled, SubjectState.Randomized, SubjectState.Active],
            SubjectState.Unblinded => [SubjectState.Enrolled, SubjectState.Randomized, SubjectState.Unblinded],
            SubjectState.Completed => [SubjectState.Enrolled, SubjectState.Randomized, SubjectState.Active, SubjectState.Completed],
            SubjectState.Withdrawn => [SubjectState.Withdrawn],
            SubjectState.ScreenFailed => [SubjectState.ScreenFailed],
            SubjectState.ReconsentRequired => [SubjectState.ReconsentRequired],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled SubjectState in test setup."),
        };

        foreach (var step in path)
        {
            subject.TransitionTo(step).IsSuccess.Should().BeTrue(
                $"test setup must reach {status} through a legal transition path (step: {step})");
        }
    }

    private static SubjectDemographicsUpdateRequest DemographicsOnly(string name = "Jane Doe") =>
        new(new DemographicsPayload(name, Birthdate: null, Gender: null, Race: null), StratFactors: null);

    private static SubjectDemographicsUpdateRequest StratFactorsOnly(string site) =>
        new(Demographics: null, StratFactors: new Dictionary<string, object?> { ["site"] = site });

    // -- Not-found ----------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.UpdateDemographicsAsync("NO-SUCH-SUBJECT", DemographicsOnly(),
            "reason", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    public async Task DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.DeleteDemographicsAsync("NO-SUCH-SUBJECT", "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.UpdateDemographicsAsync(subject.Id.ToString(), DemographicsOnly(),
            "reason", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    // -- SCN-004: pre-randomization strat-factors update succeeds ------------------------------------

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-003",
            stratFactors: new Dictionary<string, object?> { ["site"] = "A" });

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            StratFactorsOnly("B"), "Stratification factor correction pre-randomization",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-003", TestContext.Current.CancellationToken);
        persisted!.StratFactors["site"].Should().Be("B");
    }

    // -- SCN-006: pre-randomization demographics update succeeds -------------------------------------

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-004", encryptedDemographics: "original-cipher");

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            DemographicsOnly("Follow-up Vitals Update"), "Follow-up Vitals Update",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EncryptedDemographics.Should().NotBe("original-cipher");
        result.Value.EncryptedDemographics.Should().StartWith("FAKE-CIPHER:");
        harness.Encryption.EncryptCallCount.Should().Be(1);

        harness.AuditLog.Calls.Should().ContainSingle();
        harness.AuditLog.Calls[0].Action.Should().Be("UPDATE_DEMOGRAPHICS");
        harness.AuditLog.Calls[0].EntityName.Should().Be("ClinicalSubject");
    }

    [Fact]
    public async Task UpdateDemographicsAsync_ReturnsTheSubjectsRealState()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-004");

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            DemographicsOnly(), "reason", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("SCREENING");
        result.Value.ActiveProtocolVersion.Should().Be("1.0.0");
        result.Value.SiteId.Should().BeNull();
        result.Value.TreatmentGroup.Should().BeNull();
    }

    // -- SCN-004: post-randomization lock matrix ------------------------------------------------------

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-010",
            stratFactors: new Dictionary<string, object?> { ["site"] = "B" }, status: SubjectState.Randomized);

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            StratFactorsOnly("C"), "Attempted re-stratification", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        harness.AuditLog.Calls.Should().BeEmpty("a failed lock-mutation attempt writes no audit entry");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-010", TestContext.Current.CancellationToken);
        persisted!.StratFactors["site"].Should().Be("B", "a rejected mutation must never change the persisted value");
    }

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation()
    {
        // INV-011: genuinely changed payload, correctly-decryptable existing ciphertext -> the nominal
        // LOCKED_FACTOR_MUTATION case (contrast the corrupted-ciphertext regression test below).
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-011",
            encryptedDemographics: "decryptable-existing-cipher", status: SubjectState.Randomized);

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            DemographicsOnly(), "Attempted demographics change", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        harness.Encryption.DecryptedCiphertexts.Should().Contain("decryptable-existing-cipher");
    }

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-015",
            encryptedDemographics: "existing-cipher",
            stratFactors: new Dictionary<string, object?> { ["site"] = "B" }, status: SubjectState.Active);

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            new SubjectDemographicsUpdateRequest(
                new DemographicsPayload("Jane Doe", null, null, null),
                new Dictionary<string, object?> { ["site"] = "C" }),
            "reason", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        harness.Encryption.EncryptCallCount.Should().Be(0,
            "the strat-factors check (raise site #21) runs before demographics (raise site #23) and must short-circuit first");
        harness.Encryption.DecryptedCiphertexts.Should().BeEmpty(
            "the existing-ciphertext decrypt only happens once the demographics branch is actually reached");
    }

    // -- Raise site #22 regression: corrupted existing ciphertext must fail loudly -------------------

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation()
    {
        // Raise site #22's explicit fix: the legacy silently defaulted a corrupted existing ciphertext to
        // current_demo={}, which spuriously looked like a genuine change and mis-reported
        // LOCKED_FACTOR_MUTATION. This port must fail loudly instead -- a materially different error
        // *type* (a thrown exception, not a Result<T> domain-error code) from the lock-mutation case.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-019",
            encryptedDemographics: CorruptedCiphertext, status: SubjectState.Active);

        var act = () => harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            DemographicsOnly(), "Routine follow-up update", TestContext.Current.CancellationToken);

        // A materially different error *type* from the lock-mutation case: a thrown CLR exception the
        // caller must observe as a faulted Task, never a Result<SubjectResponse>.Failure with any DomainError
        // code at all -- LOCKED_FACTOR_MUTATION included.
        await act.Should().ThrowAsync<DemographicsDecryptionFailedException>();
        harness.AuditLog.Calls.Should().BeEmpty("a thrown decryption failure must not be masked as a completed, audited mutation");
    }

    [Fact]
    public async Task UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting()
    {
        // The existing-ciphertext decrypt-and-discard step is scoped to the post-randomization lock check
        // only (raise site #22 is specifically about that check) -- a pre-randomization update must succeed
        // normally and must never even attempt to decrypt a stale, possibly-corrupted existing value.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-020", encryptedDemographics: CorruptedCiphertext);

        var result = await harness.Service.UpdateDemographicsAsync(subject.SubjectCode.Value,
            DemographicsOnly(), "reason", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        harness.Encryption.DecryptedCiphertexts.Should().BeEmpty();
    }

    // -- DeleteDemographicsAsync: SCN-004 boundary check -----------------------------------------------

    [Fact]
    public async Task DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsRealStateWithClearedDemographics()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", encryptedDemographics: "some-cipher");

        var result = await harness.Service.DeleteDemographicsAsync(subject.SubjectCode.Value, "reason",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EncryptedDemographics.Should().BeNull();
        result.Value.Status.Should().Be("SCREENING");
        result.Value.ActiveProtocolVersion.Should().Be("1.0.0");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.EncryptedDemographics.Should().BeNull();
    }

    [Fact]
    public async Task DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-012",
            encryptedDemographics: "some-cipher", status: SubjectState.Active);

        var result = await harness.Service.DeleteDemographicsAsync(subject.SubjectCode.Value, "Attempted soft delete",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SOFT_DELETE_BLOCKED");
        result.Error.Code.Should().NotBe("LOCKED_FACTOR_MUTATION",
            "SoftDeleteBlocked and LockedFactorMutation must remain distinct error codes despite sharing the same IsPostRandomization() guard");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-012", TestContext.Current.CancellationToken);
        persisted!.EncryptedDemographics.Should().Be("some-cipher", "a rejected delete must never clear the persisted value");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task DeleteDemographicsAsync_OnSuccess_WritesDeleteDemographicsAuditEntry()
    {
        // #4767: the legacy delete path wrote no audit entry; every mutation now does.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", encryptedDemographics: "some-cipher");

        var result = await harness.Service.DeleteDemographicsAsync(subject.SubjectCode.Value, "reason",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var call = harness.AuditLog.Calls.Should().ContainSingle().Which;
        call.Action.Should().Be("DELETE_DEMOGRAPHICS");
        call.EntityName.Should().Be("ClinicalSubject");
        call.RecordId.Should().Be(subject.Id.ToString());
        call.UserId.Should().Be(harness.AuditContext.UserId);
        call.ChangeReason.Should().Be("reason");
    }

    [Fact]
    public async Task DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", encryptedDemographics: "some-cipher");

        var result = await harness.Service.DeleteDemographicsAsync(subject.Id.ToString(), "reason",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }
}
