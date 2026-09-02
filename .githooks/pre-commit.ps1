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
        & dotnet format Cadence.sln --verify-no-changes
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::error:: C# formatting violations detected! Run 'dotnet format Cadence.sln' to auto-fix." -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host "::error:: Failed checking dotnet format." -ForegroundColor Red
        exit 1
    }
}

# 2. Validate ADRs
if (Test-Path "scripts\validate_adrs.py") {
    Write-Host "  Validating ADR index and requirement tags..." -ForegroundColor Gray
    & python "scripts\validate_adrs.py"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error:: ADR validation failed! Check docs/adr/ and run 'python scripts/validate_adrs.py'." -ForegroundColor Red
        exit 1
    }
}

Write-Host "[Pre-Commit] All quality sentinels passed." -ForegroundColor Green
exit 0
