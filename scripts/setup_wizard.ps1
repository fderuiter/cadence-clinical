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
Write-Host "  Installing/validating the .NET 10 SDK, global tools, and dev certs..." -ForegroundColor Gray
try {
    & (Join-Path $RepoRoot "scripts\setup_dotnet_env.ps1")
}
catch {
    Write-Failed ".NET SDK setup step failed ($_) — continuing with remaining stages."
}
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
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
        & pnpm install --frozen-lockfile
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "pnpm workspace dependencies installed."
        }
        else {
            Write-Failed "pnpm install failed — see output above. Continuing with remaining stages."
        }
    }
    else {
        Write-Info "pnpm not found even after 'corepack enable'; install it manually (npm i -g pnpm)."
    }
}

# 3. Python / uv
Write-Host "`n[3/6] Python & uv" -ForegroundColor Cyan
Write-Host "  Syncing the Python toolchain, pre-commit hooks, and (optionally) Playwright..." -ForegroundColor Gray
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Info "uv not found on PATH. Install it: irm https://astral.sh/uv/install.ps1 | iex"
}
else {
    & uv sync --python 3.14 --all-extras
    if ($LASTEXITCODE -eq 0) { Write-Ok "uv sync complete." }
    else { Write-Failed "uv sync failed — see output above. Continuing with remaining stages." }

    & uv run --python 3.14 pre-commit install --install-hooks
    if ($LASTEXITCODE -eq 0) { Write-Ok "pre-commit hooks installed." }
    else { Write-Failed "pre-commit hook install failed — see output above." }

    if ($Minimal) {
        Write-Info "Skipped Playwright browser download (-Minimal). Run 'uv run playwright install chromium' later if needed."
    }
    else {
        & uv run --python 3.14 playwright install chromium
        if ($LASTEXITCODE -eq 0) { Write-Ok "Playwright Chromium installed." }
        else { Write-Failed "Playwright browser install failed — see output above." }
    }
}

# 4. Local datastores
Write-Host "`n[4/6] Clinical Datastores" -ForegroundColor Cyan
Write-Host "  Probing PostgreSQL (5432) and Neo4j (7687)..." -ForegroundColor Gray
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
else { Write-Info "PostgreSQL not reachable. Start via: docker compose up -d postgres" }
if (Test-TcpPort 7687) { Write-Ok "Neo4j Bolt running on port 7687." }
else { Write-Info "Neo4j not reachable. Start via: docker compose up -d neo4j" }

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
if (Get-Command uv -ErrorAction SilentlyContinue) {
    & uv run --python 3.14 cadence doctor
    if ($LASTEXITCODE -ne 0) {
        Write-Failed "'cadence doctor' reported issues above — review before continuing."
    }
}
else {
    Write-Info "Skipped 'cadence doctor' (uv not available)."
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
