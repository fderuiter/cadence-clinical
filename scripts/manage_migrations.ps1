<#
.SYNOPSIS
    Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (PowerShell)

.DESCRIPTION
    Thin wrapper forwarding EF Core database workflow commands to `cadence db` across all
    18 persistence modules (Requirements: PRD-SYS-053, ADR-2498).
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("add", "script", "bundle", "apply", "seed", "list", "optimize")]
    [string]$Action,

    [Parameter(Position = 1, Mandatory = $true)]
    [string]$Service,

    [Parameter(Position = 2)]
    [string]$MigrationName
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$cliProject = Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj"
$subcommand = if ($Action -eq "apply") { "migrate" } else { $Action }

if ($subcommand -eq "add") {
    if ([string]::IsNullOrWhiteSpace($MigrationName)) {
        Write-Host "Error: MigrationName is required for 'add'." -ForegroundColor Red
        exit 1
    }
    & dotnet run --project $cliProject -- db add $Service $MigrationName
} else {
    & dotnet run --project $cliProject -- db $subcommand $Service
}

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
