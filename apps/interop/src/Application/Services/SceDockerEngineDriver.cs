using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Interop.Application.Services;

public record SandboxExecutionRequest(
    string Language,
    string SourceCode,
    string LockfileContent,
    string LockType,
    int MaxMemoryMb = 2048,
    int TimeoutSeconds = 300,
    bool NetworkIsolated = true,
    bool FilesystemReadonlyRoot = true,
    string? ContainerImageOverride = null,
    Dictionary<string, string>? EnvironmentVariables = null
);

public record GeneratedArtifactDto(
    string ArtifactId,
    string Name,
    string ArtifactType,
    string FilePath,
    string MimeType,
    long SizeBytes,
    string Sha256Digest,
    string? ContentPreview
);

public record SandboxExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    string ContainerImageDigest,
    List<GeneratedArtifactDto> Artifacts,
    string OutputArtifactsDigest
);

public interface ISceDockerEngineDriver
{
    Task<SandboxExecutionResult> ExecuteInSandboxAsync(SandboxExecutionRequest request, CancellationToken ct = default);
}

public class SceDockerEngineDriver : ISceDockerEngineDriver
{
    public async Task<SandboxExecutionResult> ExecuteInSandboxAsync(SandboxExecutionRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string tempDir = Path.Combine(Path.GetTempPath(), "cadence_sce_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string ext = request.Language.Equals("R", StringComparison.OrdinalIgnoreCase) ? "R" : "py";
            string scriptPath = Path.Combine(tempDir, $"script.{ext}");
            await File.WriteAllTextAsync(scriptPath, request.SourceCode, ct);

            string imageName = request.ContainerImageOverride ??
                (request.Language.Equals("R", StringComparison.OrdinalIgnoreCase) ? "cadence-sce-r:latest" : "cadence-sce-py:latest");

            var dockerResult = await ExecuteDockerContainerAsync(request, tempDir, imageName, ct);
            stopwatch.Stop();

            return dockerResult with { DurationMs = stopwatch.ElapsedMilliseconds };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    private static async Task<SandboxExecutionResult> ExecuteDockerContainerAsync(
        SandboxExecutionRequest request,
        string tempDir,
        string imageName,
        CancellationToken ct)
    {
        try
        {
            string cmdRunner = request.Language.Equals("R", StringComparison.OrdinalIgnoreCase)
                ? "Rscript /workspace/script.R"
                : "python /workspace/script.py";

            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--rm");
            if (request.NetworkIsolated)
            {
                psi.ArgumentList.Add("--network=none");
            }
            if (request.FilesystemReadonlyRoot)
            {
                psi.ArgumentList.Add("--read-only");
            }
            psi.ArgumentList.Add("--tmpfs");
            psi.ArgumentList.Add("/tmp:rw,noexec,nosuid,size=64m");
            psi.ArgumentList.Add("--memory");
            psi.ArgumentList.Add($"{request.MaxMemoryMb}m");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add($"{tempDir}:/workspace:rw");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("/workspace");

            if (request.EnvironmentVariables != null)
            {
                foreach (var (k, v) in request.EnvironmentVariables)
                {
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add($"{k}={v}");
                }
            }

            psi.ArgumentList.Add(imageName);
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cmdRunner);

            using var process = new Process { StartInfo = psi };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            if (!process.Start())
            {
                return CreateFailureResult("Failed to start docker process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            await process.WaitForExitAsync(cts.Token);

            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            if (process.ExitCode == 125 || process.ExitCode == 127 || stdout.Contains("Unable to find image") || stderr.Contains("Unable to find image"))
            {
                string errorMsg;
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    errorMsg = stderr.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(stdout))
                {
                    errorMsg = stdout.Trim();
                }
                else
                {
                    errorMsg = $"Docker container execution failed with exit code {process.ExitCode}.";
                }

                int failureExitCode = process.ExitCode != 0 ? process.ExitCode : 127;
                return CreateFailureResult($"Container runtime execution failed: {errorMsg}", failureExitCode);
            }

            var artifacts = CollectArtifacts(tempDir);
            string artifactsDigest = ComputeArtifactsMerkleDigest(artifacts);
            string imageDigest = ComputeSha256("docker:" + imageName);

            return new SandboxExecutionResult(
                ExitCode: process.ExitCode,
                Stdout: stdout,
                Stderr: stderr,
                DurationMs: 0,
                ContainerImageDigest: imageDigest,
                Artifacts: artifacts,
                OutputArtifactsDigest: artifactsDigest
            );
        }
        catch (Exception ex)
        {
            return CreateFailureResult($"Container sandbox execution error: {ex.Message}");
        }
    }

    private static SandboxExecutionResult CreateFailureResult(string errorMessage, int exitCode = 127)
    {
        var emptyArtifacts = new List<GeneratedArtifactDto>();
        return new SandboxExecutionResult(
            ExitCode: exitCode != 0 ? exitCode : 127,
            Stdout: string.Empty,
            Stderr: errorMessage,
            DurationMs: 0,
            ContainerImageDigest: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            Artifacts: emptyArtifacts,
            OutputArtifactsDigest: ComputeArtifactsMerkleDigest(emptyArtifacts)
        );
    }

    private static List<GeneratedArtifactDto> CollectArtifacts(string dirPath)
    {
        var artifacts = new List<GeneratedArtifactDto>();
        if (!Directory.Exists(dirPath)) return artifacts;

        var files = Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith("script.")) continue;

            var fi = new FileInfo(file);
            string content = File.ReadAllText(file);
            string sha256 = ComputeSha256(content);
            string ext = Path.GetExtension(file).ToLowerInvariant();

            string artifactType = ext switch
            {
                ".csv" or ".tsv" => "DATASET",
                ".json" or ".xml" => "TABLE",
                ".png" or ".jpg" or ".pdf" or ".svg" => "FIGURE",
                ".txt" or ".log" => "LOG",
                _ => "TABLE"
            };

            string mimeType = ext switch
            {
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                _ => "text/plain"
            };

            string preview = content.Length > 2048 ? content[..2048] + "..." : content;

            artifacts.Add(new GeneratedArtifactDto(
                ArtifactId: "art-" + Guid.NewGuid().ToString("N")[..12],
                Name: fileName,
                ArtifactType: artifactType,
                FilePath: Path.GetRelativePath(dirPath, file),
                MimeType: mimeType,
                SizeBytes: fi.Length,
                Sha256Digest: sha256,
                ContentPreview: preview
            ));
        }

        return artifacts;
    }

    private static string ComputeArtifactsMerkleDigest(List<GeneratedArtifactDto> artifacts)
    {
        if (artifacts.Count == 0) return ComputeSha256("empty_artifacts");
        var sortedDigests = artifacts.Select(a => a.Sha256Digest).OrderBy(d => d).ToList();
        return ComputeSha256(string.Join("|", sortedDigests));
    }

    private static string ComputeSha256(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
