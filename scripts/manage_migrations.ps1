<#
.SYNOPSIS
    Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (PowerShell)

.DESCRIPTION
    Thin wrapper forwarding EF Core database and migration workflow commands to
    `cadence db` and `cadence migrations` across all 18 persistence modules
    (Requirements: PRD-SYS-053, ADR-2233, ADR-2447, ADR-2498, ADR-2536).
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("add", "script", "verify", "list", "bundle", "apply", "seed", "optimize")]
    [string]$Action,

    [Parameter(Position = 1)]
    [string]$Service = "all",

    [Parameter(Position = 2)]
    [string]$MigrationName
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$cliProject = Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj"
$target = if ([string]::IsNullOrWhiteSpace($Service)) { "all" } else { $Service }

if ($Action -eq "add") {
    if ([string]::IsNullOrWhiteSpace($Service) -or $Service.ToLowerInvariant() -eq "all" -or [string]::IsNullOrWhiteSpace($MigrationName)) {
        Write-Host "Error: Specific module name and MigrationName are required for 'add'." -ForegroundColor Red
        exit 1
    }
    & dotnet run --project $cliProject -- db add $Service $MigrationName
} elseif ($Action -eq "verify") {
    & dotnet run --project $cliProject -- migrations verify $target
} else {
    $subcommand = if ($Action -eq "apply") { "migrate" } else { $Action }
    & dotnet run --project $cliProject -- db $subcommand $target
}

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
