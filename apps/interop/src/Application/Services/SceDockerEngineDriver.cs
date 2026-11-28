using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Storage;

namespace Cadence.Apps.Interop.Application.Services;

public record SandboxExecutionRequest(
    string Language,
    string SourceCode,
    string LockfileContent,
    string LockType,
    int MaxMemoryMb = SceDockerEngineDriver.DefaultMemoryMb,
    int TimeoutSeconds = SceDockerEngineDriver.DefaultTimeoutSeconds,
    bool NetworkIsolated = true,
    bool FilesystemReadonlyRoot = true,
    string? ContainerImageOverride = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    long MaxOutputBytes = SceDockerEngineDriver.DefaultMaxOutputBytes
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
    private readonly IScratchWorkspaceProvider _scratchWorkspace;

    public SceDockerEngineDriver(IScratchWorkspaceProvider scratchWorkspace)
    {
        ArgumentNullException.ThrowIfNull(scratchWorkspace);
        _scratchWorkspace = scratchWorkspace;
    }

    public const string DefaultPlaceholderImageDigest = "sha256:cadence-sce-r4.4-py3.14-validated-base-2026";
    public const string DefaultPythonImage = "cadence-sce-py:latest";
    public const string DefaultRImage = "cadence-sce-r:latest";

    public const int MinMemoryMb = 256;
    public const int DefaultMemoryMb = 2048;
    public const int MaxMemoryMbLimit = 8192;

    public const int MinTimeoutSeconds = 1;
    public const int DefaultTimeoutSeconds = 300;
    public const int MaxTimeoutSecondsLimit = 3600;

    public const long DefaultMaxOutputBytes = 10L * 1024 * 1024;
    public const long MaxOutputBytesLimit = 10L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ApprovedImagesByLanguage =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PYTHON"] = new HashSet<string>(StringComparer.Ordinal)
            {
                DefaultPythonImage,
            },
            ["R"] = new HashSet<string>(StringComparer.Ordinal)
            {
                DefaultRImage,
            },
        };

    internal static int ClampMemoryMb(int memoryMb) =>
        Math.Clamp(memoryMb, MinMemoryMb, MaxMemoryMbLimit);

    internal static int ClampTimeoutSeconds(int timeoutSeconds) =>
        Math.Clamp(timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSecondsLimit);

    internal static long ClampMaxOutputBytes(long maxOutputBytes) =>
        maxOutputBytes <= 0
            ? DefaultMaxOutputBytes
            : Math.Min(maxOutputBytes, MaxOutputBytesLimit);

    internal static bool TryResolveApprovedImage(
        string? language,
        string? requestedImage,
        out string resolvedImage,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(language)
            || !ApprovedImagesByLanguage.TryGetValue(language, out var allowedImages))
        {
            resolvedImage = string.Empty;
            error = $"Unsupported statistical computing language '{language}'.";
            return false;
        }

        string defaultImage = language.Equals("R", StringComparison.OrdinalIgnoreCase)
            ? DefaultRImage
            : DefaultPythonImage;

        if (string.IsNullOrWhiteSpace(requestedImage)
            || string.Equals(requestedImage, DefaultPlaceholderImageDigest, StringComparison.Ordinal))
        {
            resolvedImage = defaultImage;
            error = string.Empty;
            return true;
        }

        if (requestedImage.StartsWith('-') || !allowedImages.Contains(requestedImage))
        {
            resolvedImage = string.Empty;
            error = $"Container image '{requestedImage}' is not in the approved SCE allow-list for language '{language.ToUpperInvariant()}'.";
            return false;
        }

        resolvedImage = requestedImage;
        error = string.Empty;
        return true;
    }

    internal static IReadOnlyList<string> BuildDockerArguments(
        SandboxExecutionRequest request,
        string tempDir,
        string imageName)
    {
        int clampedMemoryMb = ClampMemoryMb(request.MaxMemoryMb);
        string cmdRunner = request.Language.Equals("R", StringComparison.OrdinalIgnoreCase)
            ? "Rscript /workspace/script.R"
            : "python /workspace/script.py";

        var args = new List<string>
        {
            "run",
            "--rm",
            "--network=none",
            "--read-only",
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",
            "--tmpfs",
            "/tmp:rw,noexec,nosuid,size=64m",
            "--memory",
            $"{clampedMemoryMb}m",
            "-v",
            $"{tempDir}:/workspace:rw",
            "-w",
            "/workspace",
        };

        if (request.EnvironmentVariables != null)
        {
            foreach (var (k, v) in request.EnvironmentVariables)
            {
                if (IsValidEnvironmentVariableName(k))
                {
                    args.Add("-e");
                    args.Add($"{k}={v}");
                }
            }
        }

        args.Add("--");
        args.Add(imageName);
        args.Add("sh");
        args.Add("-c");
        args.Add(cmdRunner);

        return args;
    }

    public async Task<SandboxExecutionResult> ExecuteInSandboxAsync(SandboxExecutionRequest request, CancellationToken ct = default)
    {
        if (!TryResolveApprovedImage(request.Language, request.ContainerImageOverride, out string imageName, out string imageError))
        {
            return CreateFailureResult(imageError, exitCode: 126);
        }

        var stopwatch = Stopwatch.StartNew();
        string tempDir = _scratchWorkspace.CreateDirectory("cadence_sce");

        try
        {
            string ext = request.Language.Equals("R", StringComparison.OrdinalIgnoreCase) ? "R" : "py";
            string scriptPath = Path.Combine(tempDir, $"script.{ext}");
            await File.WriteAllTextAsync(scriptPath, request.SourceCode, ct);

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
            int timeoutSeconds = ClampTimeoutSeconds(request.TimeoutSeconds);
            long maxOutputBytes = ClampMaxOutputBytes(request.MaxOutputBytes);

            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string arg in BuildDockerArguments(request, tempDir, imageName))
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) => AppendBoundedLine(stdoutBuilder, e.Data, maxOutputBytes);
            process.ErrorDataReceived += (_, e) => AppendBoundedLine(stderrBuilder, e.Data, maxOutputBytes);

            if (!process.Start())
            {
                return CreateFailureResult("Failed to start docker process.", 127);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await process.WaitForExitAsync(cts.Token);

            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            if (process.ExitCode == 125 || process.ExitCode == 127 || stdout.Contains("Unable to find image", StringComparison.Ordinal) || stderr.Contains("Unable to find image", StringComparison.Ordinal))
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

            var artifacts = CollectArtifacts(tempDir, maxOutputBytes);
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
            return CreateFailureResult($"Container sandbox execution error: {ex.Message}", 1);
        }
    }

    internal static async Task<SandboxExecutionResult> ExecuteFallbackSandboxAsync(
        SandboxExecutionRequest request,
        string scriptPath,
        string tempDir,
        CancellationToken ct)
    {
        if (!TryResolveApprovedImage(request.Language, request.ContainerImageOverride, out _, out string imageError))
        {
            return CreateFailureResult(imageError, exitCode: 126);
        }

        int timeoutSeconds = ClampTimeoutSeconds(request.TimeoutSeconds);
        long maxOutputBytes = ClampMaxOutputBytes(request.MaxOutputBytes);
        string runner = request.Language.Equals("R", StringComparison.OrdinalIgnoreCase) ? "Rscript" : "python3";
        var psi = new ProcessStartInfo
        {
            FileName = runner,
            UseShellExecute = false,
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(scriptPath);

        if (request.EnvironmentVariables != null)
        {
            foreach (var (k, v) in request.EnvironmentVariables)
            {
                if (IsValidEnvironmentVariableName(k))
                {
                    psi.EnvironmentVariables[k] = v;
                }
            }
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return CreateFailureResult($"Failed to start local process '{runner}'.", 1);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cts.Token);

            var artifacts = CollectArtifacts(tempDir, maxOutputBytes);
            string artifactsDigest = ComputeArtifactsMerkleDigest(artifacts);

            return new SandboxExecutionResult(
                ExitCode: process.ExitCode,
                Stdout: stdoutTask.Result,
                Stderr: stderrTask.Result,
                DurationMs: 0,
                ContainerImageDigest: DefaultPlaceholderImageDigest,
                Artifacts: artifacts,
                OutputArtifactsDigest: artifactsDigest
            );
        }
        catch (Exception ex)
        {
            return CreateFailureResult($"Fallback sandbox execution error: {ex.Message}", 1);
        }
    }

    private static SandboxExecutionResult CreateFailureResult(string errorMessage, int exitCode = 1)
    {
        var emptyArtifacts = new List<GeneratedArtifactDto>();
        return new SandboxExecutionResult(
            ExitCode: exitCode != 0 ? exitCode : 1,
            Stdout: string.Empty,
            Stderr: errorMessage,
            DurationMs: 0,
            ContainerImageDigest: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            Artifacts: emptyArtifacts,
            OutputArtifactsDigest: ComputeArtifactsMerkleDigest(emptyArtifacts)
        );
    }

    internal static List<GeneratedArtifactDto> CollectArtifacts(string dirPath, long maxOutputBytes = DefaultMaxOutputBytes)
    {
        var artifacts = new List<GeneratedArtifactDto>();
        if (string.IsNullOrWhiteSpace(dirPath) || maxOutputBytes <= 0 || !Directory.Exists(dirPath))
        {
            return artifacts;
        }

        long effectiveMaxBytes = Math.Min(maxOutputBytes, MaxOutputBytesLimit);
        var rootInfo = new DirectoryInfo(dirPath);
        if (IsSymbolicLinkOrReparsePoint(rootInfo))
        {
            return artifacts;
        }

        string canonicalRoot = Path.GetFullPath(rootInfo.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        long totalBytesCollected = 0;
        var pendingDirs = new Stack<DirectoryInfo>();
        pendingDirs.Push(rootInfo);

        while (pendingDirs.Count > 0)
        {
            var currentDir = pendingDirs.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = currentDir.EnumerateFileSystemInfos()
                    .OrderBy(e => e.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // Push subdirectories in reverse alphabetical order so Pop visits them in deterministic order.
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i] is DirectoryInfo subDir && !IsSymbolicLinkOrReparsePoint(subDir))
                {
                    string canonicalSubDir = Path.GetFullPath(subDir.FullName);
                    if (IsUnder(canonicalSubDir, canonicalRoot))
                    {
                        pendingDirs.Push(subDir);
                    }
                }
            }

            foreach (var entry in entries)
            {
                if (entry is not FileInfo fileInfo)
                {
                    continue;
                }

                if (fileInfo.Name.StartsWith("script.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsSymbolicLinkOrReparsePoint(fileInfo)
                    || !TryResolveContainedFilePath(fileInfo, canonicalRoot, out string canonicalFile))
                {
                    continue;
                }

                long remainingBytes = effectiveMaxBytes - totalBytesCollected;
                if (remainingBytes < 0)
                {
                    break;
                }

                if (!TryReadBoundedFileBytes(fileInfo, canonicalFile, remainingBytes, out byte[] fileBytes))
                {
                    continue;
                }

                totalBytesCollected += fileBytes.Length;
                string content = Encoding.UTF8.GetString(fileBytes);
                string sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
                string ext = Path.GetExtension(canonicalFile).ToLowerInvariant();

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
                    Name: fileInfo.Name,
                    ArtifactType: artifactType,
                    FilePath: Path.GetRelativePath(canonicalRoot, canonicalFile),
                    MimeType: mimeType,
                    SizeBytes: fileBytes.LongLength,
                    Sha256Digest: sha256,
                    ContentPreview: preview
                ));
            }
        }

        return artifacts;
    }

    private static bool IsSymbolicLinkOrReparsePoint(FileSystemInfo info)
    {
        try
        {
            if (info.LinkTarget is not null)
            {
                return true;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            return info.ResolveLinkTarget(returnFinalTarget: true) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryResolveContainedFilePath(FileInfo fileInfo, string canonicalRoot, out string canonicalFile)
    {
        canonicalFile = Path.GetFullPath(fileInfo.FullName);
        if (!IsUnder(canonicalFile, canonicalRoot))
        {
            return false;
        }

        var parent = fileInfo.Directory;
        while (parent is not null)
        {
            string parentPath = Path.GetFullPath(parent.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(parentPath, canonicalRoot, StringComparison.Ordinal))
            {
                return !IsSymbolicLinkOrReparsePoint(parent);
            }

            if (!IsUnder(parentPath, canonicalRoot) || IsSymbolicLinkOrReparsePoint(parent))
            {
                return false;
            }

            parent = parent.Parent;
        }

        return false;
    }

    private static bool TryReadBoundedFileBytes(
        FileInfo fileInfo,
        string canonicalFile,
        long maxBytesToRead,
        out byte[] bytes)
    {
        bytes = [];
        try
        {
            fileInfo.Refresh();
            if (!fileInfo.Exists || IsSymbolicLinkOrReparsePoint(fileInfo))
            {
                return false;
            }

            long length = fileInfo.Length;
            if (length < 0 || length > maxBytesToRead || length > int.MaxValue - 1)
            {
                return false;
            }

            using var stream = new FileStream(canonicalFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 0 || stream.Length > maxBytesToRead)
            {
                return false;
            }

            int limit = (int)maxBytesToRead;
            var buffer = new byte[(int)Math.Min(stream.Length + 1, limit + 1L)];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > limit)
                {
                    return false;
                }
            }

            bytes = totalRead == buffer.Length ? buffer : buffer[..totalRead];
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static bool IsValidEnvironmentVariableName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static void AppendBoundedLine(StringBuilder builder, string? line, long maxBytes)
    {
        if (line is null || builder.Length >= maxBytes)
        {
            return;
        }

        long remaining = maxBytes - builder.Length;
        if (line.Length + Environment.NewLine.Length <= remaining)
        {
            builder.AppendLine(line);
        }
        else if (remaining > 0)
        {
            builder.Append(line.AsSpan(0, (int)Math.Min(line.Length, remaining)));
        }
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
