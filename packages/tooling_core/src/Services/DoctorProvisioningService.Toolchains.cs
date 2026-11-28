using System;
using System.Collections.Generic;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static partial class DoctorProvisioningService
{
    private static bool VerifyToolchainPrerequisites(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        bool ok = true;

        var dotnetProbe = runtime.RunProcess("dotnet", ["--version"], repoRoot, 15_000);
        if (dotnetProbe.ExitCode != 0 || string.IsNullOrWhiteSpace(dotnetProbe.StdOut))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: false,
                Blocking: true,
                ".NET 10 SDK was not found on PATH."));
            ok = false;
        }
        else if (!dotnetProbe.StdOut.StartsWith("10.", StringComparison.Ordinal))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: false,
                Blocking: true,
                $"Active .NET SDK ({dotnetProbe.StdOut}) does not satisfy the .NET 10 requirement in global.json."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "dotnet",
                Succeeded: true,
                Blocking: true,
                $".NET 10 SDK {dotnetProbe.StdOut} active."));
        }

        var nodeProbe = runtime.RunProcess("node", ["--version"], repoRoot, 15_000);
        if (nodeProbe.ExitCode != 0 || string.IsNullOrWhiteSpace(nodeProbe.StdOut))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "node",
                Succeeded: false,
                Blocking: true,
                "Node.js was not found on PATH. Install Node.js 22.13+ or 24+ before running setup."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "node",
                Succeeded: true,
                Blocking: true,
                $"Node.js {nodeProbe.StdOut} active."));
        }

        string packageJsonPath = Path.Combine(repoRoot, "package.json");
        string? pinnedPnpm = File.Exists(packageJsonPath)
            ? ReadPinnedPnpmVersion(File.ReadAllText(packageJsonPath))
            : null;

        var pnpmProbe = runtime.RunProcess("pnpm", ["--version"], repoRoot, 15_000);
        string? installedPnpm = pnpmProbe.ExitCode == 0 && !string.IsNullOrWhiteSpace(pnpmProbe.StdOut)
            ? pnpmProbe.StdOut.Trim()
            : null;

        bool needsCorepack = installedPnpm is null ||
            (pinnedPnpm is not null && !string.Equals(installedPnpm, pinnedPnpm, StringComparison.Ordinal));
        if (needsCorepack && runtime.ResolveExecutable("corepack", repoRoot) is not null)
        {
            _ = runtime.RunProcess("corepack", ["enable"], repoRoot, 30_000);
            pnpmProbe = runtime.RunProcess("pnpm", ["--version"], repoRoot, 15_000);
            installedPnpm = pnpmProbe.ExitCode == 0 && !string.IsNullOrWhiteSpace(pnpmProbe.StdOut)
                ? pnpmProbe.StdOut.Trim()
                : null;
        }

        if (installedPnpm is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                "pnpm is missing or crashes. Install Corepack ('npm install --global corepack') and run 'corepack enable'."));
            ok = false;
        }
        else if (File.Exists(packageJsonPath) && pinnedPnpm is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                "package.json declares no pnpm packageManager pin."));
            ok = false;
        }
        else if (pinnedPnpm is not null && !string.Equals(installedPnpm, pinnedPnpm, StringComparison.Ordinal))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: false,
                Blocking: true,
                $"pnpm {installedPnpm} does not match package.json pin {pinnedPnpm}. Run 'corepack enable'."));
            ok = false;
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm",
                Succeeded: true,
                Blocking: true,
                $"pnpm {installedPnpm} active."));
        }

        return ok;
    }

    private static void ProvisionLocalDotnetTools(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        string manifestPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        if (File.Exists(manifestPath))
        {
            var restoreResult = runtime.RunProcess("dotnet", ["tool", "restore"], repoRoot, 180_000);
            if (restoreResult.ExitCode != 0)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "dotnet-tools",
                    Succeeded: false,
                    Blocking: true,
                    $"dotnet tool restore failed: {restoreResult.CombinedOutput}"));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "dotnet-tools",
                Succeeded: true,
                Blocking: true,
                "Local .NET tools restored from .config/dotnet-tools.json."));
        }

        string solutionPath = Path.Combine(repoRoot, "Cadence.sln");
        if (File.Exists(solutionPath))
        {
            var slnRestore = runtime.RunProcess("dotnet", ["restore", solutionPath], repoRoot, 300_000);
            if (slnRestore.ExitCode != 0)
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "dotnet-restore",
                    Succeeded: false,
                    Blocking: true,
                    $"dotnet restore Cadence.sln failed: {slnRestore.CombinedOutput}"));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "dotnet-restore",
                Succeeded: true,
                Blocking: true,
                "Restored NuGet packages for Cadence.sln."));
        }
    }

    private static void ProvisionFrontendWorkspace(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        string lockfilePath = Path.Combine(repoRoot, "pnpm-lock.yaml");
        if (!File.Exists(lockfilePath))
        {
            return;
        }

        var installResult = runtime.RunProcess("pnpm", ["install", "--frozen-lockfile"], repoRoot, 600_000);
        if (installResult.ExitCode != 0)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "pnpm-install",
                Succeeded: false,
                Blocking: true,
                $"pnpm install --frozen-lockfile failed: {installResult.CombinedOutput}"));
            return;
        }

        steps.Add(new DoctorProvisioningStepResult(
            "pnpm-install",
            Succeeded: true,
            Blocking: true,
            "pnpm workspace dependencies verified."));
    }

    private static void ProvisionDetectSecretsVenv(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        bool isWindows = runtime.Platform == ProvisioningPlatform.Windows;
        string venvDir = Path.Combine(repoRoot, ".cadence", "venv");
        string binSubdir = isWindows ? "Scripts" : "bin";
        string hookExeName = isWindows ? "detect-secrets-hook.exe" : "detect-secrets-hook";
        string pythonExeName = isWindows ? "python.exe" : "python";
        string hookPath = Path.Combine(venvDir, binSubdir, hookExeName);
        string venvPython = Path.Combine(venvDir, binSubdir, pythonExeName);
        string pinnedSpec = $"detect-secrets=={DetectSecretsGateService.PinnedVersion}";

        if (File.Exists(hookPath))
        {
            var existingProbe = runtime.RunProcess(hookPath, ["--version"], repoRoot, 15_000);
            if (existingProbe.ExitCode == 0 && existingProbe.CombinedOutput.Contains(DetectSecretsGateService.PinnedVersion, StringComparison.Ordinal))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "detect-secrets",
                    Succeeded: true,
                    Blocking: true,
                    $"detect-secrets {DetectSecretsGateService.PinnedVersion} already provisioned in .cadence/venv."));
                return;
            }
        }

        if (runtime.ResolveExecutable("uv", repoRoot) is not null)
        {
            var uvVenv = runtime.RunProcess("uv", ["venv", venvDir], repoRoot, 120_000);
            var uvInstall = uvVenv.ExitCode == 0
                ? runtime.RunProcess("uv", ["pip", "install", "--python", venvPython, "--quiet", pinnedSpec], repoRoot, 180_000)
                : uvVenv;

            if (uvInstall.ExitCode == 0 && VerifyDetectSecretsHook(hookPath, repoRoot, runtime))
            {
                steps.Add(new DoctorProvisioningStepResult(
                    "detect-secrets",
                    Succeeded: true,
                    Blocking: true,
                    $"detect-secrets {DetectSecretsGateService.PinnedVersion} provisioned in .cadence/venv via uv."));
                return;
            }

            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: false,
                Blocking: true,
                $"Failed to provision {pinnedSpec} in .cadence/venv via uv: {uvInstall.CombinedOutput}"));
            return;
        }

        string? pythonCmd = isWindows
            ? runtime.ResolveExecutable("python", repoRoot) ?? runtime.ResolveExecutable("python3", repoRoot)
            : runtime.ResolveExecutable("python3", repoRoot) ?? runtime.ResolveExecutable("python", repoRoot);

        if (pythonCmd is null)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: false,
                Blocking: true,
                $"Neither 'uv' nor 'python3'/'python' was found on PATH to provision {pinnedSpec} in .cadence/venv."));
            return;
        }

        var venvCreate = runtime.RunProcess(pythonCmd, ["-m", "venv", venvDir], repoRoot, 120_000);
        var pipInstall = venvCreate.ExitCode == 0
            ? runtime.RunProcess(venvPython, ["-m", "pip", "install", "--quiet", "--upgrade", "pip", pinnedSpec], repoRoot, 240_000)
            : venvCreate;

        if (pipInstall.ExitCode == 0 && VerifyDetectSecretsHook(hookPath, repoRoot, runtime))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "detect-secrets",
                Succeeded: true,
                Blocking: true,
                $"detect-secrets {DetectSecretsGateService.PinnedVersion} provisioned in .cadence/venv via python -m venv."));
            return;
        }

        steps.Add(new DoctorProvisioningStepResult(
            "detect-secrets",
            Succeeded: false,
            Blocking: true,
            $"Failed to provision {pinnedSpec} in .cadence/venv: {pipInstall.CombinedOutput}"));
    }

    private static bool VerifyDetectSecretsHook(string hookPath, string repoRoot, IDoctorProvisioningRuntime runtime)
    {
        if (!File.Exists(hookPath))
        {
            return false;
        }

        var probe = runtime.RunProcess(hookPath, ["--version"], repoRoot, 15_000);
        return probe.ExitCode == 0 && probe.CombinedOutput.Contains(DetectSecretsGateService.PinnedVersion, StringComparison.Ordinal);
    }

    private static void ProvisionEnvFile(string repoRoot, List<DoctorProvisioningStepResult> steps)
    {
        string envPath = Path.Combine(repoRoot, ".env");
        string envExamplePath = Path.Combine(repoRoot, ".env.example");
        if (File.Exists(envPath))
        {
            steps.Add(new DoctorProvisioningStepResult(
                "env-file",
                Succeeded: true,
                Blocking: false,
                ".env file is present."));
            return;
        }

        if (File.Exists(envExamplePath))
        {
            File.Copy(envExamplePath, envPath, overwrite: false);
            steps.Add(new DoctorProvisioningStepResult(
                "env-file",
                Succeeded: true,
                Blocking: false,
                "Initialized .env from .env.example."));
        }
    }

    private static void ProvisionOptionalPlaywrightChromium(
        string repoRoot,
        IDoctorProvisioningRuntime runtime,
        List<DoctorProvisioningStepResult> steps)
    {
        if (!File.Exists(Path.Combine(repoRoot, "apps", "web", "package.json")))
        {
            return;
        }

        var pwResult = runtime.RunProcess("pnpm", ["--filter", "web", "exec", "playwright", "install", "chromium"], repoRoot, 600_000);
        if (pwResult.ExitCode == 0)
        {
            steps.Add(new DoctorProvisioningStepResult(
                "playwright",
                Succeeded: true,
                Blocking: false,
                "Playwright Chromium browser installed for web E2E testing."));
        }
        else
        {
            steps.Add(new DoctorProvisioningStepResult(
                "playwright",
                Succeeded: false,
                Blocking: false,
                "Playwright browser install skipped (run 'pnpm --filter web exec playwright install chromium' later if needed)."));
        }
    }
}
