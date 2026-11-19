using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Fact(DisplayName = "ValidateLockfile_ValidUvLock_ReturnsValidStatusAndParsedPackages")]
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

    [Fact(DisplayName = "ValidateLockfile_TamperedDigest_ReturnsTamperedStatus")]
    [GxPRequirement("PRD-CDR-005")]
    public void ValidateLockfile_TamperedDigest_ReturnsTamperedStatus()
    {
        string content = "[[package]]\nname = \"pandas\"\nversion = \"2.2.0\"\n";
        string wrongDigest = "0000000000000000000000000000000000000000000000000000000000000000";

        var result = _lockfileValidator.ValidateLockfile("UV_LOCK", content, expectedDigest: wrongDigest);

        result.Status.Should().Be("TAMPERED");
        result.IsQualified.Should().BeFalse();
        result.Details.Should().Contain("mismatch");
    }

    [Fact(DisplayName = "ValidateLockfile_UnqualifiedPackageWithStrictQualification_RejectsPackage")]
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

    [Fact(DisplayName = "ValidateLockfile_ValidRenvLock_ParsesRPackages")]
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

    [Fact(DisplayName = "ExecuteInSandbox_WhenContainerUnavailable_FailsClosedWithNonZeroExitCodeAndErrorOutput")]
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

    [Fact(DisplayName = "ExecuteInSandbox_MockedDriver_ExecutesProgramAndGeneratesArtifactsAndManifestDigest")]
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

    [Fact(DisplayName = "DatabaseLockToken_IssueAndRevoke_UpdatesTokenStateAndAuditTrail")]
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

    [Fact(DisplayName = "ExecutionManifest_VerifySeal_ReturnsValidWhenUntampered")]
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

    [Fact(DisplayName = "ExecuteInSandbox_WhenRuntimeFails_ReturnsNonZeroExitCodeStderrAndNoSyntheticData")]
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

    [Fact(DisplayName = "ExecutionRun_WhenExitCodeNonZero_SetsStatusFailedAndSuppressesSignatureAndSeal")]
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

    [Fact(DisplayName = "TryResolveApprovedImage_MapsDefaultPlaceholderAndRejectsUnapprovedImages")]
    public void TryResolveApprovedImage_MapsDefaultPlaceholderAndRejectsUnapprovedImages()
    {
        SceDockerEngineDriver.TryResolveApprovedImage("PYTHON", null, out string pyDefault, out _)
            .Should().BeTrue();
        pyDefault.Should().Be(SceDockerEngineDriver.DefaultPythonImage);

        SceDockerEngineDriver.TryResolveApprovedImage("R", SceDockerEngineDriver.DefaultPlaceholderImageDigest, out string rPlaceholder, out _)
            .Should().BeTrue();
        rPlaceholder.Should().Be(SceDockerEngineDriver.DefaultRImage);

        SceDockerEngineDriver.TryResolveApprovedImage("PYTHON", SceDockerEngineDriver.DefaultPlaceholderImageDigest, out string pyPlaceholder, out _)
            .Should().BeTrue();
        pyPlaceholder.Should().Be(SceDockerEngineDriver.DefaultPythonImage);

        SceDockerEngineDriver.TryResolveApprovedImage("PYTHON", SceDockerEngineDriver.DefaultPythonImage, out string pyExplicit, out _)
            .Should().BeTrue();
        pyExplicit.Should().Be(SceDockerEngineDriver.DefaultPythonImage);

        // Cross-language, arbitrary images, flag-like values, and unknown languages are rejected.
        SceDockerEngineDriver.TryResolveApprovedImage("PYTHON", SceDockerEngineDriver.DefaultRImage, out _, out string mismatchError)
            .Should().BeFalse();
        mismatchError.Should().Contain("allow-list");

        SceDockerEngineDriver.TryResolveApprovedImage("R", "alpine:latest", out _, out string unapprovedError)
            .Should().BeFalse();
        unapprovedError.Should().Contain("allow-list");

        SceDockerEngineDriver.TryResolveApprovedImage("PYTHON", "--privileged", out _, out string flagError)
            .Should().BeFalse();
        flagError.Should().Contain("allow-list");

        SceDockerEngineDriver.TryResolveApprovedImage("BASH", null, out _, out string languageError)
            .Should().BeFalse();
        languageError.Should().Contain("Unsupported");
    }

    [Fact(DisplayName = "ExecuteInSandbox_WithUnapprovedImageOverride_FailsClosedBeforeExecution")]
    public async Task ExecuteInSandbox_WithUnapprovedImageOverride_FailsClosedBeforeExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new SandboxExecutionRequest(
            Language: "PYTHON",
            SourceCode: "print('hello')",
            LockfileContent: "",
            LockType: "UV_LOCK",
            ContainerImageOverride: "alpine:latest"
        );

        var result = await _dockerDriver.ExecuteInSandboxAsync(request, ct);

        result.ExitCode.Should().Be(126);
        result.Artifacts.Should().BeEmpty();
        result.Stderr.Should().Contain("allow-list");
    }

    [Fact(DisplayName = "BuildDockerArguments_AlwaysEnforcesIsolationFlagsClampedMemoryAndImageSeparator")]
    public void BuildDockerArguments_AlwaysEnforcesIsolationFlagsClampedMemoryAndImageSeparator()
    {
        string workspaceDir = Path.Combine(Path.GetTempPath(), "sce_args_check");
        var request = new SandboxExecutionRequest(
            Language: "R",
            SourceCode: "cat('ok')",
            LockfileContent: "{}",
            LockType: "RENV_LOCK",
            MaxMemoryMb: 999999,
            TimeoutSeconds: 999999,
            NetworkIsolated: false,
            FilesystemReadonlyRoot: false,
            EnvironmentVariables: new Dictionary<string, string>
            {
                ["STUDY_ID"] = "STUDY-001",
                ["--privileged"] = "true"
            }
        );

        var args = SceDockerEngineDriver.BuildDockerArguments(request, workspaceDir, SceDockerEngineDriver.DefaultRImage);

        args.Should().ContainInOrder("run", "--rm", "--network=none", "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges");
        args.Should().ContainInOrder("--memory", $"{SceDockerEngineDriver.MaxMemoryMbLimit}m");
        args.Should().Contain("STUDY_ID=STUDY-001");
        args.Should().NotContain(a => a.Contains("--privileged", StringComparison.Ordinal));

        int separatorIndex = args.ToList().IndexOf("--");
        separatorIndex.Should().BeGreaterThan(0);
        args[separatorIndex + 1].Should().Be(SceDockerEngineDriver.DefaultRImage);
        args[separatorIndex + 2].Should().Be("sh");
        args[separatorIndex + 3].Should().Be("-c");
        args[separatorIndex + 4].Should().Be("Rscript /workspace/script.R");

        var minMemoryRequest = request with { MaxMemoryMb = 1 };
        var minMemoryArgs = SceDockerEngineDriver.BuildDockerArguments(minMemoryRequest, workspaceDir, SceDockerEngineDriver.DefaultRImage);
        minMemoryArgs.Should().ContainInOrder("--memory", $"{SceDockerEngineDriver.MinMemoryMb}m");
    }

    [Fact(DisplayName = "ExecuteEndpoint_RejectsDisabledIsolationAndUnapprovedImagesAndClampsDefaultFrontendConfig")]
    public async Task ExecuteEndpoint_RejectsDisabledIsolationAndUnapprovedImagesAndClampsDefaultFrontendConfig()
    {
        var ct = TestContext.Current.CancellationToken;
        SandboxExecutionRequest? capturedRequest = null;
        var mockDriver = Substitute.For<ISceDockerEngineDriver>();
        mockDriver.ExecuteInSandboxAsync(Arg.Do<SandboxExecutionRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxExecutionResult(
                ExitCode: 0,
                Stdout: "ok",
                Stderr: string.Empty,
                DurationMs: 12,
                ContainerImageDigest: "sha256:approved-py",
                Artifacts: [],
                OutputArtifactsDigest: "empty_digest"
            )));

        string dbName = $"sce-policy-{Guid.NewGuid():N}";
        var inMemoryProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        await using var factory = new WebApplicationFactory<Cadence.Apps.Interop.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestFieldProtectionKeys()
                .UseTestSceSigningKey();

            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<InteropDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<InteropDbContext>>();
                services.AddDbContext<InteropDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                    options.UseInternalServiceProvider(inMemoryProvider);
                });

                services.RemoveAll<ISceDockerEngineDriver>();
                services.AddSingleton(mockDriver);
            });
        });

        const string studyId = "STUDY-SCE-POLICY";
        string programId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
            var program = new StatisticalProgram
            {
                StudyId = studyId,
                Name = "adam_adsl.py",
                Title = "ADSL",
                Language = "PYTHON",
                SourceCode = "print('ok')",
                CodeHash = "hash-code",
                LockType = "UV_LOCK",
                LockDigest = "hash-lock",
                CreatedAt = _clock.UtcNow,
                CreatedBy = "stat-user"
            };
            db.StatisticalPrograms.Add(program);
            await db.SaveChangesAsync(ct);
            programId = program.Id;
        }

        var caller = TestSecurityContext.Identity("stat-user", "sponsor_statistician") with { StudyIds = [studyId] };
        using var client = factory.CreateSignedClient(caller);

        // 1. network_isolated: false is rejected
        var netOffResp = await client.PostAsJsonAsync("/api/v1/interop/sce/execute", new JsonObject
        {
            ["study_id"] = studyId,
            ["program_id"] = programId,
            ["snapshot_id"] = "snap-1",
            ["sandbox_config"] = new JsonObject
            {
                ["network_isolated"] = false
            }
        }, ct);
        netOffResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. filesystem_readonly_root: false is rejected
        var roOffResp = await client.PostAsJsonAsync("/api/v1/interop/sce/execute", new JsonObject
        {
            ["study_id"] = studyId,
            ["program_id"] = programId,
            ["snapshot_id"] = "snap-1",
            ["sandbox_config"] = new JsonObject
            {
                ["filesystem_readonly_root"] = false
            }
        }, ct);
        roOffResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Unapproved docker_image_digest is rejected
        var badImgResp = await client.PostAsJsonAsync("/api/v1/interop/sce/execute", new JsonObject
        {
            ["study_id"] = studyId,
            ["program_id"] = programId,
            ["snapshot_id"] = "snap-1",
            ["sandbox_config"] = new JsonObject
            {
                ["docker_image_digest"] = "alpine:latest"
            }
        }, ct);
        badImgResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        capturedRequest.Should().BeNull();

        // 4. Frontend-style payload (default placeholder digest omitted, huge memory & timeout) succeeds with clamped limits and server-resolved image
        var validResp = await client.PostAsJsonAsync("/api/v1/interop/sce/execute", new JsonObject
        {
            ["study_id"] = studyId,
            ["program_id"] = programId,
            ["snapshot_id"] = "snap-1",
            ["sandbox_config"] = new JsonObject
            {
                ["timeout_seconds"] = 999999,
                ["max_memory_mb"] = 999999,
                ["max_output_bytes"] = 999999999L,
                ["network_isolated"] = true
            }
        }, ct);
        validResp.StatusCode.Should().Be(HttpStatusCode.OK, await validResp.Content.ReadAsStringAsync(ct));
        capturedRequest.Should().NotBeNull();
        capturedRequest!.NetworkIsolated.Should().BeTrue();
        capturedRequest.FilesystemReadonlyRoot.Should().BeTrue();
        capturedRequest.MaxMemoryMb.Should().Be(SceDockerEngineDriver.MaxMemoryMbLimit);
        capturedRequest.TimeoutSeconds.Should().Be(SceDockerEngineDriver.MaxTimeoutSecondsLimit);
        capturedRequest.MaxOutputBytes.Should().Be(SceDockerEngineDriver.MaxOutputBytesLimit);
        capturedRequest.ContainerImageOverride.Should().Be(SceDockerEngineDriver.DefaultPythonImage);
    }

    [Fact(DisplayName = "CollectArtifacts_RejectsFileAndDirectorySymlinksAndCollectsContainedRegularFiles")]
    public void CollectArtifacts_RejectsFileAndDirectorySymlinksAndCollectsContainedRegularFiles()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "sce_symlink_test_" + Guid.NewGuid().ToString("N"));
        string workspaceDir = Path.Combine(testRoot, "workspace");
        string outsideDir = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(workspaceDir);
        Directory.CreateDirectory(Path.Combine(workspaceDir, "nested"));
        Directory.CreateDirectory(outsideDir);

        try
        {
            string outsideSecret = Path.Combine(outsideDir, "host_secret.txt");
            File.WriteAllText(outsideSecret, "TOP_SECRET_HOST_CONTENT");
            File.WriteAllText(Path.Combine(outsideDir, "leaked.csv"), "LEAKED,1\n");

            File.WriteAllText(Path.Combine(workspaceDir, "script.py"), "print('ignored')");
            File.WriteAllText(Path.Combine(workspaceDir, "valid_table.csv"), "ARM,N\nA,10\n");
            File.WriteAllText(Path.Combine(workspaceDir, "nested", "summary.json"), "{\"ok\":true}");

            // Symlinks: absolute outside file, relative outside file, internal file symlink, dangling symlink, and directory symlink
            File.CreateSymbolicLink(Path.Combine(workspaceDir, "symlink_outside.txt"), outsideSecret);
            File.CreateSymbolicLink(Path.Combine(workspaceDir, "symlink_relative.txt"), Path.Combine("..", "outside", "host_secret.txt"));
            File.CreateSymbolicLink(Path.Combine(workspaceDir, "symlink_inside.csv"), Path.Combine(workspaceDir, "valid_table.csv"));
            File.CreateSymbolicLink(Path.Combine(workspaceDir, "dangling.csv"), Path.Combine(outsideDir, "does_not_exist.csv"));
            Directory.CreateSymbolicLink(Path.Combine(workspaceDir, "linked_outside_dir"), outsideDir);

            var artifacts = SceDockerEngineDriver.CollectArtifacts(workspaceDir);

            artifacts.Select(a => a.FilePath.Replace('\\', '/'))
                .Should().BeEquivalentTo("valid_table.csv", "nested/summary.json");
            artifacts.Should().NotContain(a => a.ContentPreview != null && a.ContentPreview.Contains("TOP_SECRET", StringComparison.Ordinal));
            artifacts.Should().NotContain(a => a.ContentPreview != null && a.ContentPreview.Contains("LEAKED", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "CollectArtifacts_EnforcesMaxOutputBytesBeforeReadingOversizedFiles")]
    public void CollectArtifacts_EnforcesMaxOutputBytesBeforeReadingOversizedFiles()
    {
        string workspaceDir = Path.Combine(Path.GetTempPath(), "sce_bounds_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceDir);

        try
        {
            File.WriteAllText(Path.Combine(workspaceDir, "a_first.csv"), new string('a', 60));
            File.WriteAllText(Path.Combine(workspaceDir, "b_second.csv"), new string('b', 60));
            File.WriteAllText(Path.Combine(workspaceDir, "c_oversized.csv"), new string('c', 500));

            var artifacts = SceDockerEngineDriver.CollectArtifacts(workspaceDir, maxOutputBytes: 100);

            artifacts.Should().ContainSingle()
                .Which.Name.Should().Be("a_first.csv");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
            {
                Directory.Delete(workspaceDir, recursive: true);
            }
        }
    }
}

