<#
.SYNOPSIS
    Cadence Clinical Platform - Full-Stack Developer Environment Setup Wizard (Windows / PowerShell)

.DESCRIPTION
    Provisions everything a fresh checkout needs across .NET, Node, and Python — on a local
    workstation, GitHub Codespaces, a devcontainer, or CI. Safe to re-run any time.
    Requirements: PRD-SYS-053, ADR-2233

.PARAMETER NonInteractive
    Skip prompts and accept all defaults.

.PARAMETER Minimal
    Skip heavy/optional steps (Playwright browser download).
#>

[CmdletBinding()]
param(
    [switch]$NonInteractive,
    [switch]$Minimal
)

$ErrorActionPreference = "Continue"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location $RepoRoot

if ($env:CI -eq "true" -or $env:CLAUDE_CODE_REMOTE -eq "true" -or -not [Environment]::UserInteractive) {
    $NonInteractive = $true
}

$FailedStages = New-Object System.Collections.Generic.List[string]
function Write-Ok($msg)     { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Info($msg)   { Write-Host "  [INFO] $msg" -ForegroundColor DarkYellow }
function Write-Failed($msg) { $FailedStages.Add($msg); Write-Info $msg }
# A failure that breaks every later command (for example pnpm): reported in red and makes the wizard exit non-zero.
$script:BlockingFailure = $false
function Write-Blocking($msg) { $FailedStages.Add($msg); $script:BlockingFailure = $true; Write-Host "  [ERROR] $msg" -ForegroundColor Red }

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   CADENCE CLINICAL PLATFORM — FULL-STACK DEVELOPER SETUP WIZARD                 " -ForegroundColor Cyan
Write-Host "================================================================================`n" -ForegroundColor Cyan

$Context = "local workstation"
if ($env:CLAUDE_CODE_REMOTE -eq "true") { $Context = "Claude Code remote session" }
elseif ($env:CODESPACES) { $Context = "GitHub Codespaces" }
elseif ($env:REMOTE_CONTAINERS -or $env:DEVCONTAINER) { $Context = "VS Code devcontainer" }
elseif ($env:CI -eq "true") { $Context = "CI runner" }
Write-Host "  Detected environment: $Context (Windows)" -ForegroundColor Gray
Write-Host "  Mode: $(if ($NonInteractive) { 'non-interactive' } else { 'interactive' })`n" -ForegroundColor Gray

if (-not $NonInteractive) {
    $start = Read-Host "Ready to begin full-stack setup? [Y/n]"
    if ($start -match '^[nN]') {
        Write-Host "Setup paused. Re-run anytime with: scripts/setup_wizard.ps1"
        exit 0
    }
}

# 1. .NET 10 SDK
Write-Host "`n[1/6] .NET 10 SDK" -ForegroundColor Cyan
Write-Host "  Installing/validating the .NET 10 SDK, local tools, and dev certs..." -ForegroundColor Gray
try {
    & (Join-Path $RepoRoot "scripts\setup_dotnet_env.ps1")
}
catch {
    Write-Failed ".NET SDK setup step failed ($_) — continuing with remaining stages."
}
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Push-Location $RepoRoot
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            Write-Failed "dotnet tool restore failed (exit code $LASTEXITCODE)."
        }
    }
    finally {
        Pop-Location
    }
    & dotnet restore (Join-Path $RepoRoot "Cadence.sln")
    if ($LASTEXITCODE -ne 0) {
        Write-Info "Some projects failed to restore (see Directory.Packages.props pins)."
    }
}

# 2. Node.js / pnpm
Write-Host "`n[2/6] Node.js & pnpm" -ForegroundColor Cyan
Write-Host "  Enabling corepack and installing workspace dependencies..." -ForegroundColor Gray
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Info "Node.js not found on PATH. Install it, then re-run this wizard."
}
else {
    Write-Ok "Node.js $(node --version) detected."
    if (Get-Command corepack -ErrorAction SilentlyContinue) {
        & corepack enable 2>$null | Out-Null
    }
    if (Get-Command pnpm -ErrorAction SilentlyContinue) {
        # package.json pins pnpm with packageManager (#5331); corepack runs exactly that version.
        $pnpmVersion = & pnpm --version 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Blocking "pnpm is on the PATH but crashes. Run 'corepack enable' so pnpm follows the packageManager pin in package.json."
        }
        else {
            & pnpm install --frozen-lockfile
            if ($LASTEXITCODE -eq 0) {
                Write-Ok "pnpm ${pnpmVersion}: workspace dependencies installed."
            }
            else {
                Write-Blocking "pnpm install failed — see output above. Every 'pnpm cadence' command needs it."
            }
        }
    }
    else {
        Write-Blocking "pnpm not found. Install Corepack if needed ('npm install --global corepack'), then run 'corepack enable'."
    }
}

# 3. Git pre-commit hooks
Write-Host "`n[3/6] Git Quality Gates" -ForegroundColor Cyan
Write-Host "  Pointing git at the unified pre-commit hook (.githooks/pre-commit) and provisioning detect-secrets..." -ForegroundColor Gray
& git -C $RepoRoot config core.hooksPath .githooks
if ($LASTEXITCODE -eq 0) { Write-Ok "Native git pre-commit quality hooks enabled (.githooks/pre-commit -> cadence check --pre-commit)." }
else { Write-Failed "Failed to configure .githooks. Run 'git config core.hooksPath .githooks' manually." }

$CadenceVenv = Join-Path $RepoRoot ".cadence\venv"
$DetectSecretsHook = Join-Path $CadenceVenv "Scripts\detect-secrets-hook.exe"
$PipAuditExe = Join-Path $CadenceVenv "Scripts\pip-audit.exe"
$VenvReady = $false
if ((Test-Path $DetectSecretsHook) -and (Test-Path $PipAuditExe)) {
    $dsVersion = & $DetectSecretsHook --version 2>$null
    $dsOk = ($LASTEXITCODE -eq 0)
    & $PipAuditExe --version 2>$null | Out-Null
    if ($dsOk -and ($LASTEXITCODE -eq 0)) {
        Write-Ok "detect-secrets ${dsVersion} and pip-audit already provisioned in .cadence\venv."
        $VenvReady = $true
    }
}
if (-not $VenvReady) {
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        & uv venv $CadenceVenv 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & uv pip install --python (Join-Path $CadenceVenv "Scripts\python.exe") --quiet detect-secrets==1.5.0 pip-audit==2.10.1
        }
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "detect-secrets 1.5.0 and pip-audit 2.10.1 provisioned in .cadence\venv via uv."
        }
        else {
            Write-Blocking "Failed to provision detect-secrets==1.5.0 and pip-audit==2.10.1 in .cadence\venv via uv."
        }
    }
    elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv $CadenceVenv
        if ($LASTEXITCODE -eq 0) {
            & (Join-Path $CadenceVenv "Scripts\python.exe") -m pip install --quiet --upgrade pip detect-secrets==1.5.0 pip-audit==2.10.1
        }
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "detect-secrets 1.5.0 and pip-audit 2.10.1 provisioned in .cadence\venv via python -m venv."
        }
        else {
            Write-Blocking "Failed to provision detect-secrets==1.5.0 and pip-audit==2.10.1 in .cadence\venv."
        }
    }
    else {
        Write-Blocking "Neither 'uv' nor 'python' was found on PATH to provision detect-secrets==1.5.0 and pip-audit==2.10.1 for pre-commit guardrails."
    }
}

$CadenceBin = if ($env:CADENCE_BIN_DIR) { $env:CADENCE_BIN_DIR } else { Join-Path $RepoRoot ".cadence\bin" }
New-Item -ItemType Directory -Force -Path $CadenceBin | Out-Null

$ArchName = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$ActionlintArch = if ($ArchName -eq "arm64") { "arm64" } else { "amd64" }
$PinnedActionlintSha = if ($ActionlintArch -eq "arm64") {
    "cadcf7ea4efe3a68728893813643cebe1185e5b1d4be5b96245f65c9a4d5ea41" # pragma: allowlist secret
}
else {
    "6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9" # pragma: allowlist secret
}
$PinnedHadolintSha = "01d927294962b5387f9ead4f18679158452be4f17c765ad0bdffe5264b9c7b0a" # pragma: allowlist secret
$PinnedShellcheckSha = "8a4e35ab0b331c85d73567b12f2a444df187f483e5079ceffa6bda1faa2e740e" # pragma: allowlist secret

$ActionlintExe = Join-Path $CadenceBin "actionlint.exe"
$ActionlintReady = $false
if ((Test-Path $ActionlintExe) -and -not $env:ACTIONLINT_SHA256) {
    $alVer = & $ActionlintExe -version 2>$null
    if ($LASTEXITCODE -eq 0 -and ($alVer -join "`n") -match "1\.7\.12") {
        Write-Ok "actionlint 1.7.12 already provisioned in .cadence\bin."
        $ActionlintReady = $true
    }
}
if (-not $ActionlintReady) {
    $ActionlintAsset = "actionlint_1.7.12_windows_${ActionlintArch}.zip"
    $ActionlintUrl = if ($env:ACTIONLINT_DOWNLOAD_URL) { $env:ACTIONLINT_DOWNLOAD_URL } else { "https://github.com/rhysd/actionlint/releases/download/v1.7.12/${ActionlintAsset}" }
    $ExpectedActionlintSha = (if ($env:ACTIONLINT_SHA256) { $env:ACTIONLINT_SHA256 } else { $PinnedActionlintSha }).ToLowerInvariant()
    $TmpActionlintDir = Join-Path ([System.IO.Path]::GetTempPath()) ("cadence-actionlint-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $TmpActionlintDir | Out-Null
    $TmpActionlintZip = Join-Path $TmpActionlintDir $ActionlintAsset
    try {
        Invoke-WebRequest -Uri $ActionlintUrl -OutFile $TmpActionlintZip -UseBasicParsing
        $ActualActionlintSha = (Get-FileHash -Path $TmpActionlintZip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($ActualActionlintSha -ne $ExpectedActionlintSha) {
            Write-Blocking "Checksum verification failed for ${ActionlintAsset} (expected ${ExpectedActionlintSha}, got ${ActualActionlintSha})."
        }
        else {
            Expand-Archive -Path $TmpActionlintZip -DestinationPath $TmpActionlintDir -Force
            Copy-Item -Path (Join-Path $TmpActionlintDir "actionlint.exe") -Destination $ActionlintExe -Force
            Write-Ok "actionlint 1.7.12 SHA-256 verified and installed in .cadence\bin."
        }
    }
    catch {
        Write-Blocking "Failed to provision actionlint 1.7.12 in .cadence\bin: $_"
    }
    finally {
        Remove-Item -Path $TmpActionlintDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$HadolintExe = Join-Path $CadenceBin "hadolint.exe"
$HadolintReady = $false
if ((Test-Path $HadolintExe) -and -not $env:HADOLINT_SHA256) {
    $hlVer = & $HadolintExe --version 2>$null
    if ($LASTEXITCODE -eq 0 -and ($hlVer -join "`n") -match "2\.15\.1") {
        Write-Ok "hadolint 2.15.1 already provisioned in .cadence\bin."
        $HadolintReady = $true
    }
}
if (-not $HadolintReady) {
    $HadolintAsset = "hadolint-windows-x86_64.exe"
    $HadolintUrl = if ($env:HADOLINT_DOWNLOAD_URL) { $env:HADOLINT_DOWNLOAD_URL } else { "https://github.com/hadolint/hadolint/releases/download/v2.15.1/${HadolintAsset}" }
    $ExpectedHadolintSha = (if ($env:HADOLINT_SHA256) { $env:HADOLINT_SHA256 } else { $PinnedHadolintSha }).ToLowerInvariant()
    $TmpHadolint = Join-Path ([System.IO.Path]::GetTempPath()) ("cadence-hadolint-" + [Guid]::NewGuid().ToString("N") + ".exe")
    try {
        Invoke-WebRequest -Uri $HadolintUrl -OutFile $TmpHadolint -UseBasicParsing
        $ActualHadolintSha = (Get-FileHash -Path $TmpHadolint -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($ActualHadolintSha -ne $ExpectedHadolintSha) {
            Write-Blocking "Checksum verification failed for ${HadolintAsset} (expected ${ExpectedHadolintSha}, got ${ActualHadolintSha})."
        }
        else {
            Move-Item -Path $TmpHadolint -Destination $HadolintExe -Force
            Write-Ok "hadolint 2.15.1 SHA-256 verified and installed in .cadence\bin."
        }
    }
    catch {
        Write-Blocking "Failed to provision hadolint 2.15.1 in .cadence\bin: $_"
    }
    finally {
        if (Test-Path $TmpHadolint) {
            Remove-Item -Path $TmpHadolint -Force -ErrorAction SilentlyContinue
        }
    }
}

$ShellcheckExe = Join-Path $CadenceBin "shellcheck.exe"
$ShellcheckReady = $false
if ((Test-Path $ShellcheckExe) -and -not $env:SHELLCHECK_SHA256 -and -not $env:SHELLCHECK_DOWNLOAD_URL) {
    $scVer = & $ShellcheckExe --version 2>$null
    if ($LASTEXITCODE -eq 0 -and ($scVer -join "`n") -match "0\.11\.0") {
        Write-Ok "shellcheck 0.11.0 already provisioned in .cadence\bin."
        $ShellcheckReady = $true
    }
}
if (-not $ShellcheckReady) {
    $ShellcheckAsset = "shellcheck-v0.11.0.zip"
    $ShellcheckUrl = if ($env:SHELLCHECK_DOWNLOAD_URL) { $env:SHELLCHECK_DOWNLOAD_URL } else { "https://github.com/koalaman/shellcheck/releases/download/v0.11.0/${ShellcheckAsset}" }
    $ExpectedShellcheckSha = (if ($env:SHELLCHECK_SHA256) { $env:SHELLCHECK_SHA256 } else { $PinnedShellcheckSha }).ToLowerInvariant()
    $TmpShellcheckDir = Join-Path ([System.IO.Path]::GetTempPath()) ("cadence-shellcheck-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $TmpShellcheckDir | Out-Null
    $TmpShellcheckZip = Join-Path $TmpShellcheckDir $ShellcheckAsset
    try {
        Invoke-WebRequest -Uri $ShellcheckUrl -OutFile $TmpShellcheckZip -UseBasicParsing
        $ActualShellcheckSha = (Get-FileHash -Path $TmpShellcheckZip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($ActualShellcheckSha -ne $ExpectedShellcheckSha) {
            Write-Blocking "Checksum verification failed for ${ShellcheckAsset} (expected ${ExpectedShellcheckSha}, got ${ActualShellcheckSha})."
        }
        else {
            Expand-Archive -Path $TmpShellcheckZip -DestinationPath $TmpShellcheckDir -Force
            $ExtractedShellcheck = Join-Path $TmpShellcheckDir "shellcheck.exe"
            if (-not (Test-Path $ExtractedShellcheck)) {
                $ExtractedShellcheck = Join-Path $TmpShellcheckDir "shellcheck-v0.11.0\shellcheck.exe"
            }
            if (-not (Test-Path $ExtractedShellcheck)) {
                Write-Blocking "Failed to locate shellcheck.exe inside ${ShellcheckAsset}."
            }
            else {
                Copy-Item -Path $ExtractedShellcheck -Destination $ShellcheckExe -Force
                Write-Ok "shellcheck 0.11.0 SHA-256 verified and installed in .cadence\bin."
            }
        }
    }
    catch {
        Write-Blocking "Failed to provision shellcheck 0.11.0 in .cadence\bin: $_"
    }
    finally {
        Remove-Item -Path $TmpShellcheckDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Minimal) {
    Write-Info "Skipped Playwright browser download (-Minimal). Run 'pnpm --filter web exec playwright install chromium' later if needed."
}
elseif (Get-Command pnpm -ErrorAction SilentlyContinue) {
    & pnpm --filter web exec playwright install chromium
    if ($LASTEXITCODE -eq 0) { Write-Ok "Playwright Chromium installed for web E2E testing." }
    else { Write-Info "Playwright browser install skipped (install later with 'pnpm --filter web exec playwright install chromium')." }
}

# 4. Local datastores
Write-Host "`n[4/6] Clinical Datastores" -ForegroundColor Cyan
Write-Host "  Probing PostgreSQL (5432)..." -ForegroundColor Gray
function Test-TcpPort($port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect("127.0.0.1", $port, $null, $null)
        return $async.AsyncWaitHandle.WaitOne(500, $false) -and $client.Connected
    }
    catch { return $false }
    finally { $client.Close() }
}
if (Test-TcpPort 5432) { Write-Ok "PostgreSQL running on port 5432." }
else { Write-Info "PostgreSQL not reachable. Start via: docker compose -f docker/docker-compose.yml up -d postgres" }

# 5. Environment configuration (.env)
Write-Host "`n[5/6] Environment Configuration (.env)" -ForegroundColor Cyan
$envFile = Join-Path $RepoRoot ".env"
$envExample = Join-Path $RepoRoot ".env.example"
if (-not (Test-Path $envFile) -and (Test-Path $envExample)) {
    Copy-Item -Path $envExample -Destination $envFile
    Write-Ok "Initialized .env from template. Fill in secrets with: pnpm cadence setup --dev"
}
else {
    Write-Ok ".env file is present."
}

# 6. Final validation
Write-Host "`n[6/6] Final Validation" -ForegroundColor Cyan
Write-Host "  Running 'cadence doctor' across the whole stack..." -ForegroundColor Gray
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet run --project (Join-Path $RepoRoot "packages\cli\src\Cadence.Cli.csproj") -- doctor
    if ($LASTEXITCODE -ne 0) {
        Write-Failed "'cadence doctor' reported issues above — review before continuing."
    }
}
else {
    Write-Info "Skipped 'cadence doctor' (.NET SDK not available)."
}

if ($FailedStages.Count -eq 0) {
    Write-Host "`n================================================================================" -ForegroundColor Green
    Write-Host "   SETUP COMPLETE! Run 'make verify' (or 'pnpm verify') to confirm the stack.     " -ForegroundColor Green
    Write-Host "================================================================================`n" -ForegroundColor Green
}
else {
    Write-Host "`n================================================================================" -ForegroundColor Yellow
    Write-Host "   SETUP FINISHED WITH $($FailedStages.Count) ISSUE(S) — review the [INFO] lines above:" -ForegroundColor Yellow
    foreach ($msg in $FailedStages) {
        Write-Host "     - $msg" -ForegroundColor Yellow
    }
    Write-Host "================================================================================`n" -ForegroundColor Yellow
}

if ($script:BlockingFailure) { exit 1 }
