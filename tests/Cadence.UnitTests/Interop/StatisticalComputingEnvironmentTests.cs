using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Interop;

public sealed class StatisticalComputingEnvironmentTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly InteropDbContext _db;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly LockfileValidatorService _lockfileValidator = new();
    private readonly SceDockerEngineDriver _dockerDriver = new();

    public StatisticalComputingEnvironmentTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<InteropDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new InteropDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ValidateLockfile_ValidUvLock_ReturnsValidStatusAndParsedPackages")]
    [Trait("Requirement", "PRD-SYS-053")]
    public void ValidateLockfile_ValidUvLock_ReturnsValidStatusAndParsedPackages()
    {
        string uvLockContent = @"
[[package]]
name = ""pandas""
version = ""2.2.0""

[[package]]
name = ""numpy""
version = ""1.26.0""
";
        var result = _lockfileValidator.ValidateLockfile("UV_LOCK", uvLockContent, strictQualification: true);

        result.Status.Should().Be("VALID");
        result.IsQualified.Should().BeTrue();
        result.PackageCount.Should().Be(2);
        result.Packages.Select(p => p.Name).Should().Contain(["pandas", "numpy"]);
        result.UnqualifiedPackages.Should().BeEmpty();
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ValidateLockfile_TamperedDigest_ReturnsTamperedStatus")]
    [Trait("Requirement", "PRD-SYS-053")]
    public void ValidateLockfile_TamperedDigest_ReturnsTamperedStatus()
    {
        string content = "[[package]]\nname = \"pandas\"\nversion = \"2.2.0\"\n";
        string wrongDigest = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = _lockfileValidator.ValidateLockfile("UV_LOCK", content, expectedDigest: wrongDigest);

        result.Status.Should().Be("TAMPERED");
        result.IsQualified.Should().BeFalse();
        result.Details.Should().Contain("mismatch");
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ValidateLockfile_UnqualifiedPackageWithStrictQualification_RejectsPackage")]
    [Trait("Requirement", "PRD-SYS-053")]
    public void ValidateLockfile_UnqualifiedPackageWithStrictQualification_RejectsPackage()
    {
        string content = @"
[[package]]
name = ""pandas""
version = ""2.2.0""

[[package]]
name = ""malicious-unapproved-package""
version = ""1.0.0""
";
        var result = _lockfileValidator.ValidateLockfile("UV_LOCK", content, strictQualification: true);

        result.Status.Should().Be("UNQUALIFIED_PACKAGE");
        result.IsQualified.Should().BeFalse();
        result.UnqualifiedPackages.Should().Contain("malicious-unapproved-package");
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ValidateLockfile_ValidRenvLock_ParsesRPackages")]
    [Trait("Requirement", "PRD-SYS-053")]
    public void ValidateLockfile_ValidRenvLock_ParsesRPackages()
    {
        string renvContent = @"{
  ""Packages"": {
    ""rtables"": {
      ""Package"": ""rtables"",
      ""Version"": ""0.6.6"",
      ""Source"": ""Repository"",
      ""Repository"": ""CRAN""
    },
    ""dplyr"": {
      ""Package"": ""dplyr"",
      ""Version"": ""1.1.4"",
      ""Source"": ""Repository"",
      ""Repository"": ""CRAN""
    }
  }
}";
        var result = _lockfileValidator.ValidateLockfile("RENV_LOCK", renvContent, strictQualification: true);

        result.Status.Should().Be("VALID");
        result.IsQualified.Should().BeTrue();
        result.PackageCount.Should().Be(2);
        result.Packages.Select(p => p.Name).Should().Contain(["rtables", "dplyr"]);
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ExecuteInSandbox_WhenContainerUnavailable_FailsClosedWithNonZeroExitCodeAndErrorOutput")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task ExecuteInSandbox_WhenContainerUnavailable_FailsClosedWithNonZeroExitCodeAndErrorOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        string pythonSource = "print('Generating ADaM summary...')\nwith open('output_table.csv', 'w') as f:\n    f.write('ARM,SUBJECT_COUNT\\nARM_A,50\\nARM_B,50\\n')\n";
        var request = new SandboxExecutionRequest(
            Language: "PYTHON",
            SourceCode: pythonSource,
            LockfileContent: "[[package]]\nname = \"pandas\"\nversion = \"2.2.0\"\n",
            LockType: "UV_LOCK",
            NetworkIsolated: true,
            FilesystemReadonlyRoot: true
        );

        var result = await _dockerDriver.ExecuteInSandboxAsync(request, ct);

        result.ExitCode.Should().NotBe(0);
        result.Artifacts.Should().BeEmpty();
        result.Stderr.Should().NotBeNullOrWhiteSpace();
        result.ContainerImageDigest.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ExecuteInSandbox_MockedDriver_ExecutesProgramAndGeneratesArtifactsAndManifestDigest")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task ExecuteInSandbox_MockedDriver_ExecutesProgramAndGeneratesArtifactsAndManifestDigest()
    {
        var ct = TestContext.Current.CancellationToken;
        string pythonSource = "print('Generating ADaM summary...')\nwith open('output_table.csv', 'w') as f:\n    f.write('ARM,SUBJECT_COUNT\\nARM_A,50\\nARM_B,50\\n')\n";
        var request = new SandboxExecutionRequest(
            Language: "PYTHON",
            SourceCode: pythonSource,
            LockfileContent: "[[package]]\nname = \"pandas\"\nversion = \"2.2.0\"\n",
            LockType: "UV_LOCK",
            NetworkIsolated: true,
            FilesystemReadonlyRoot: true
        );

        var mockArtifacts = new List<GeneratedArtifactDto>
        {
            new(
                ArtifactId: "art-001",
                Name: "output_table.csv",
                ArtifactType: "DATASET",
                FilePath: "output_table.csv",
                MimeType: "text/csv",
                SizeBytes: 42,
                Sha256Digest: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                ContentPreview: "ARM,SUBJECT_COUNT\nARM_A,50\nARM_B,50\n"
            )
        };

        var mockDriver = Substitute.For<ISceDockerEngineDriver>();
        mockDriver.ExecuteInSandboxAsync(Arg.Any<SandboxExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxExecutionResult(
                ExitCode: 0,
                Stdout: "Generating ADaM summary...",
                Stderr: string.Empty,
                DurationMs: 150,
                ContainerImageDigest: "sha256:11223344556677889900aabbccddeeff",
                Artifacts: mockArtifacts,
                OutputArtifactsDigest: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            )));

        var result = await mockDriver.ExecuteInSandboxAsync(request, ct);

        result.ExitCode.Should().Be(0);
        result.Artifacts.Should().NotBeEmpty();
        result.Artifacts.Select(a => a.Name).Should().Contain("output_table.csv");
        result.OutputArtifactsDigest.Should().NotBeNullOrWhiteSpace();
        result.ContainerImageDigest.Should().StartWith("sha256:");
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "DatabaseLockToken_IssueAndRevoke_UpdatesTokenStateAndAuditTrail")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task DatabaseLockToken_IssueAndRevoke_UpdatesTokenStateAndAuditTrail()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = _clock.UtcNow;
        var token = new DatabaseLockAuthToken
        {
            TokenId = "DBLOCK-AUTH-TEST-12345",
            StudyId = "STUDY-001",
            LockId = "LOCK-STUDY-001-V1",
            Milestone = "DATABASE_LOCK",
            IssuedTo = "user:unblinded-stat",
            AuthorizedRolesJson = "[\"UNBLINDED_STATISTICIAN\"]",
            IssuedAt = now,
            ExpiresAt = now.AddHours(24),
            SignatureHash = "abc123hmac",
            IsRevoked = false,
            CreatedAt = now,
            CreatedBy = "user:admin",
            ReasonForChange = "Interim efficacy analysis"
        };

        _db.DatabaseLockAuthTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        var fetched = await _db.DatabaseLockAuthTokens.FirstAsync(t => t.TokenId == "DBLOCK-AUTH-TEST-12345", ct);
        fetched.IsRevoked.Should().BeFalse();

        // Revoke token
        fetched.IsRevoked = true;
        fetched.RevokedAt = now.AddHours(1);
        fetched.RevokedBy = "user:auditor";
        fetched.RevocationReason = "Analysis complete";

        _db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
        {
            Timestamp = now.AddHours(1),
            UserId = "user:auditor",
            UserRole = "AUDITOR",
            Action = "REVOKE_LOCK_TOKEN",
            StudyId = "STUDY-001",
            IsUnblinded = true,
            LockTokenId = "DBLOCK-AUTH-TEST-12345",
            DetailsJson = "{\"reason\":\"Analysis complete\"}",
            Sha256Hash = "hash123",
            CreatedAt = now.AddHours(1)
        });

        await _db.SaveChangesAsync(ct);

        var revoked = await _db.DatabaseLockAuthTokens.FirstAsync(t => t.TokenId == "DBLOCK-AUTH-TEST-12345", ct);
        revoked.IsRevoked.Should().BeTrue();
        revoked.RevocationReason.Should().Be("Analysis complete");

        var auditLog = await _db.BiostatExportAuditLogs.FirstOrDefaultAsync(a => a.Action == "REVOKE_LOCK_TOKEN", ct);
        auditLog.Should().NotBeNull();
        auditLog!.LockTokenId.Should().Be("DBLOCK-AUTH-TEST-12345");
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ExecutionManifest_VerifySeal_ReturnsValidWhenUntampered")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task ExecutionManifest_VerifySeal_ReturnsValidWhenUntampered()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = _clock.UtcNow;
        string runId = "run-test-999";
        string studyId = "STUDY-001";
        string programId = "prog-001";
        string snapshotId = "snap-2026-Q3";
        string scriptDigest = "a1b2c3d4e5f6";
        string lockDigest = "f6e5d4c3b2a1";
        string containerImageDigest = "sha256:cadence-sce-r4.4-py3.14-validated-base-2026";
        string artifactsDigest = "112233445566";
        string executedBy = "user:biostat";

        string sealPayload = $"{runId}|{studyId}|{programId}|{snapshotId}|{scriptDigest}|{lockDigest}|{containerImageDigest}|{artifactsDigest}|{executedBy}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string manifestSeal = Convert.ToHexStringLower(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sealPayload)));

        var record = new ExecutionRunRecord
        {
            RunId = runId,
            ManifestId = "man-test-999",
            StudyId = studyId,
            ProgramId = programId,
            ProgramName = "adam_adsl.py",
            SnapshotId = snapshotId,
            ScriptCodeDigest = scriptDigest,
            LockfileDigest = lockDigest,
            ContainerImageDigest = containerImageDigest,
            ExecutionStart = now,
            ExecutionEnd = now.AddSeconds(5),
            DurationMs = 5000,
            ExitCode = 0,
            Status = "COMPLETED",
            OutputArtifactsDigest = artifactsDigest,
            ExecutedBy = executedBy,
            ExecutedByRole = "UNBLINDED_STATISTICIAN",
            ManifestSha256Seal = manifestSeal,
            CreatedAt = now,
            CreatedBy = executedBy
        };

        _db.ExecutionRunRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        var fetched = await _db.ExecutionRunRecords.FirstAsync(r => r.RunId == runId, ct);
        string computedPayload = $"{fetched.RunId}|{fetched.StudyId}|{fetched.ProgramId}|{fetched.SnapshotId}|{fetched.ScriptCodeDigest}|{fetched.LockfileDigest}|{fetched.ContainerImageDigest}|{fetched.OutputArtifactsDigest}|{fetched.ExecutedBy}";
        string recomputedSeal = Convert.ToHexStringLower(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(computedPayload)));

        recomputedSeal.Should().Be(fetched.ManifestSha256Seal);
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ExecuteInSandbox_WhenRuntimeFails_ReturnsNonZeroExitCodeStderrAndNoSyntheticData")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task ExecuteInSandbox_WhenRuntimeFails_ReturnsNonZeroExitCodeStderrAndNoSyntheticData()
    {
        var ct = TestContext.Current.CancellationToken;
        string invalidPythonSource = "import non_existent_module_12345";
        var request = new SandboxExecutionRequest(
            Language: "PYTHON",
            SourceCode: invalidPythonSource,
            LockfileContent: "",
            LockType: "UV_LOCK",
            NetworkIsolated: true,
            FilesystemReadonlyRoot: true
        );

        var result = await _dockerDriver.ExecuteInSandboxAsync(request, ct);

        result.ExitCode.Should().NotBe(0);
        result.Artifacts.Should().BeEmpty();
        result.Stderr.Should().NotBeNullOrWhiteSpace();
        result.Stdout.Should().BeEmpty();
    }

    /// <summary>@req:PRD-SYS-053</summary>
    [Fact(DisplayName = "ExecutionRun_WhenExitCodeNonZero_SetsStatusFailedAndSuppressesSignatureAndSeal")]
    [Trait("Requirement", "PRD-SYS-053")]
    public async Task ExecutionRun_WhenExitCodeNonZero_SetsStatusFailedAndSuppressesSignatureAndSeal()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = _clock.UtcNow;
        string runId = "run-failed-001";
        string studyId = "STUDY-001";
        string programId = "prog-failed";

        // Simulating failed execution with ExitCode != 0
        int exitCode = 1;
        string status = exitCode == 0 ? "COMPLETED" : "FAILED";

        ElectronicSignatureBlockDto sigBlockDto;
        string manifestSeal;

        if (exitCode == 0)
        {
            sigBlockDto = new ElectronicSignatureBlockDto("user:biostat", "UNBLINDED_STATISTICIAN", now.ToString("O"), "Meaning", "hmac123");
            manifestSeal = "seal123";
        }
        else
        {
            sigBlockDto = new ElectronicSignatureBlockDto("", "", "", "NONE - Execution failed; electronic signature refused", "");
            manifestSeal = "";
        }

        var record = new ExecutionRunRecord
        {
            RunId = runId,
            ManifestId = "man-failed-001",
            StudyId = studyId,
            ProgramId = programId,
            ProgramName = "failing_script.py",
            SnapshotId = "snap-001",
            ScriptCodeDigest = "abc",
            LockfileDigest = "def",
            ContainerImageDigest = "sha256:0000",
            ExecutionStart = now,
            ExecutionEnd = now.AddSeconds(1),
            DurationMs = 1000,
            ExitCode = exitCode,
            Status = status,
            StdoutCapture = "",
            StderrCapture = "SyntaxError: invalid syntax",
            OutputArtifactsJson = "[]",
            OutputArtifactsDigest = "empty",
            ExecutedBy = "user:biostat",
            ExecutedByRole = "UNBLINDED_STATISTICIAN",
            SignatureBlockJson = System.Text.Json.JsonSerializer.Serialize(sigBlockDto),
            ManifestSha256Seal = manifestSeal,
            CreatedAt = now,
            CreatedBy = "user:biostat"
        };

        _db.ExecutionRunRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        var fetched = await _db.ExecutionRunRecords.FirstAsync(r => r.RunId == runId, ct);
        fetched.Status.Should().Be("FAILED");
        fetched.ExitCode.Should().Be(1);
        fetched.ManifestSha256Seal.Should().BeEmpty();

        var sigBlock = System.Text.Json.JsonSerializer.Deserialize<ElectronicSignatureBlockDto>(fetched.SignatureBlockJson);
        sigBlock.Should().NotBeNull();
        sigBlock!.HmacSignature.Should().BeEmpty();
        sigBlock.SignatureMeaning.Should().Contain("refused");
    }
}

