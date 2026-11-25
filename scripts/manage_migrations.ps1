<#
.SYNOPSIS
    Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (PowerShell)

.DESCRIPTION
    Thin pass-through wrapper around `cadence migrations` for generating, scripting, verifying,
    listing, bundling, optimizing, seeding, and applying Entity Framework Core 10 database migrations
    across all 18 Cadence persistence modules under 21 CFR Part 11 requirements.
    Requirements: PRD-SYS-053, ADR-2233, ADR-2447, ADR-2536
#>

[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("add", "script", "verify", "list", "bundle", "apply", "seed", "optimize")]
    [string]$Action,

    [Parameter(Position = 1)]
    [string]$Service = "all",

    [Parameter(Position = 2)]
    [string]$MigrationName,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$cliProject = Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj"

Push-Location $repoRoot
try {
    if ($Action -eq "add") {
        if ([string]::IsNullOrWhiteSpace($Service) -or $Service.ToLowerInvariant() -eq "all" -or [string]::IsNullOrWhiteSpace($MigrationName)) {
            Write-Host "Error: Specific module name and MigrationName are required for 'add'." -ForegroundColor Red
            exit 1
        }

        & dotnet run --project $cliProject -- migrations add $Service $MigrationName @ExtraArgs
    } else {
        $target = if ([string]::IsNullOrWhiteSpace($Service)) { "all" } else { $Service }
        & dotnet run --project $cliProject -- migrations $Action $target @ExtraArgs
    }

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}
