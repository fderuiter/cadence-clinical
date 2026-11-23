<#
.SYNOPSIS
    Cadence Clinical Platform - Full-Stack Developer Environment Setup Wizard (Windows / PowerShell)

.DESCRIPTION
    Thin fail-closed bootstrap wrapper around scripts/setup_dotnet_env.ps1 and
    `node scripts/run-cli.js doctor --auto-fix`.
    Requirements: PRD-SYS-053, ADR-2233, ADR-2527

.PARAMETER NonInteractive
    Skip prompts and accept all defaults.

.PARAMETER Minimal
    Skip heavy/optional steps (Playwright browser download and HTTPS dev-cert trust).

.PARAMETER LintersOnly
    Provision only SHA-256 verified actionlint, hadolint, and shellcheck in .cadence\bin.
#>

[CmdletBinding()]
param(
    [Alias("Dev", "y")]
    [switch]$NonInteractive,
    [switch]$Minimal,
    [switch]$LintersOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location $RepoRoot

if ($LintersOnly -or $env:CI -eq "true" -or $env:CLAUDE_CODE_REMOTE -eq "true" -or -not [Environment]::UserInteractive) {
    $NonInteractive = $true
}

function Stop-FailClosed([string]$Message) {
    Write-Host "  [ERROR] $Message" -ForegroundColor Red
    exit 1
}

if (-not $LintersOnly) {
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

    try {
        & (Join-Path $RepoRoot "scripts\setup_dotnet_env.ps1") -SkipCertTrust:$Minimal
        if ($LASTEXITCODE -ne 0) {
            Stop-FailClosed ".NET 10 SDK setup exited with code $LASTEXITCODE — aborting before configuring Git hooks."
        }
    }
    catch {
        Stop-FailClosed ".NET 10 SDK setup failed ($_) — aborting before configuring Git hooks."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $dotnetCandidates += $env:DOTNET_ROOT
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $dotnetCandidates += (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $dotnetCandidates += (Join-Path $env:USERPROFILE ".dotnet")
    }
    foreach ($candidate in $dotnetCandidates) {
        if ((Test-Path (Join-Path $candidate "dotnet.exe")) -or (Test-Path (Join-Path $candidate "dotnet"))) {
            $env:DOTNET_ROOT = $candidate
            $env:PATH = "$candidate$([System.IO.Path]::PathSeparator)$env:PATH"
            break
        }
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-FailClosed ".NET 10 SDK (dotnet) not found on PATH."
}
$null = & dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Stop-FailClosed ".NET 10 SDK (dotnet) failed to run '--version'."
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Stop-FailClosed "Node.js (node) not found on PATH. Install Node.js 22.13+ or 24+ and re-run setup_wizard.ps1."
}
$null = & node --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Stop-FailClosed "Node.js (node) failed to run '--version'."
}

if (Get-Command corepack -ErrorAction SilentlyContinue) {
    try {
        & corepack enable 2>$null | Out-Null
    }
    catch {
        # Ignore non-fatal corepack enable permission issues when pnpm is already active.
    }
}

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    Stop-FailClosed "pnpm not found on PATH. Install Corepack ('npm install --global corepack') and run 'corepack enable'."
}
$null = & pnpm --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Stop-FailClosed "pnpm is on PATH but failed to run '--version'. Run 'corepack enable'."
}

$doctorArgs = @((Join-Path $RepoRoot "scripts\run-cli.js"), "doctor", "--auto-fix")
if ($Minimal) {
    $doctorArgs += "--minimal"
}
if ($LintersOnly) {
    $doctorArgs += "--linters-only"
}

& node @doctorArgs
if ($LASTEXITCODE -ne 0) {
    Stop-FailClosed "'cadence doctor --auto-fix' failed with exit code $LASTEXITCODE — review the errors above."
}

if (-not $LintersOnly) {
    Write-Host "`n================================================================================" -ForegroundColor Green
    Write-Host "   SETUP COMPLETE! Run 'make verify' (or 'pnpm verify') to confirm the stack.     " -ForegroundColor Green
    Write-Host "================================================================================`n" -ForegroundColor Green
}
