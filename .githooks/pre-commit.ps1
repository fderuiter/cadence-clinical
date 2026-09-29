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
    # Format only the staged C# files; CI and 'pnpm cadence check' cover the whole solution.
    $stagedCs = @(git diff --cached --name-only --diff-filter=ACMR -- '*.cs')
    if ($stagedCs.Count -gt 0) {
        Write-Host "  Validating formatting, code style and analyzers on staged C# files..." -ForegroundColor Gray
        & dotnet format Cadence.sln --verify-no-changes --include @stagedCs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::error:: C# formatting, style or analyzer violations detected! Run 'dotnet format Cadence.sln' to auto-fix." -ForegroundColor Red
            exit 1
        }
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

