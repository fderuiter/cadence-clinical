<#
.SYNOPSIS
    Cadence Clinical Platform - Git Pre-Commit Hook (PowerShell)
    Validates C# code formatting, ADR integrity, and GxP compliance.
#>

$ErrorActionPreference = "Stop"

Write-Host "[Pre-Commit] Executing Cadence Quality Sentinels..." -ForegroundColor Cyan

# 1. Format check
$dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
if ($dotnet) {
    Write-Host "  Validating C# code style (.editorconfig)..." -ForegroundColor Gray
    try {
        & dotnet format whitespace Cadence.sln --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::error:: C# formatting violations detected! Run 'dotnet format whitespace Cadence.sln' to auto-fix." -ForegroundColor Red
            exit 1
        }
        Write-Host "  Validating C# Roslyn analyzer and code style rules..." -ForegroundColor Gray
        & dotnet format style Cadence.sln --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::error:: C# Roslyn style or analyzer violations detected! Run 'dotnet format style Cadence.sln' to auto-fix." -ForegroundColor Red
            exit 1
        }
    }
    catch {
        Write-Host "::error:: Failed checking dotnet format or Roslyn style analyzer rules." -ForegroundColor Red
        exit 1
    }
}

# 2. Run the quality gates (same as .githooks/pre-commit)
Write-Host "  Running the Cadence quality gates..." -ForegroundColor Gray
& dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check --pre-commit
if ($LASTEXITCODE -ne 0) {
    Write-Host "::error:: A quality gate failed. Run 'pnpm cadence check <gate>' for the failing gate." -ForegroundColor Red
    exit 1
}

Write-Host "[Pre-Commit] All quality sentinels passed." -ForegroundColor Green
exit 0

