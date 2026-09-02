<#
.SYNOPSIS
    Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (PowerShell)

.DESCRIPTION
    Automates generating, scripting, bundling, and applying idempotent Entity Framework Core 10
    database migrations across Cadence clinical microservices under 21 CFR Part 11 requirements.
    Requirements: PRD-SYS-053, ADR-2233
#>

[CmdletBinding()]
param(
    [Parameter(Position=0, Mandatory=$true)]
    [ValidateSet("add", "script", "bundle", "apply", "list")]
    [string]$Action,

    [Parameter(Position=1, Mandatory=$true)]
    [string]$Service,

    [Parameter(Position=2)]
    [string]$MigrationName
)

$ErrorActionPreference = "Stop"

function Capitalize-Name([string]$name) {
    return (Get-Culture).TextInfo.ToTitleCase($name.ToLowerInvariant())
}

$capName = Capitalize-Name $Service
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$serviceDir = Join-Path $repoRoot "apps\$Service"
$projectPath = Join-Path $serviceDir "src\Cadence.Apps.$capName.csproj"
$migrationsDir = "Infrastructure\Persistence\Migrations"

if (-not (Test-Path $projectPath)) {
    Write-Host "Error: Project not found at $projectPath" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Cadence EF Core Migration Manager ===" -ForegroundColor Cyan
Write-Host "Service: Cadence.Apps.$capName" -ForegroundColor Gray
Write-Host "Action:  $Action`n" -ForegroundColor Gray

switch ($Action) {
    "add" {
        if ([string]::IsNullOrWhiteSpace($MigrationName)) {
            Write-Host "Error: MigrationName is required for 'add'." -ForegroundColor Red
            exit 1
        }
        Write-Host "Generating migration '$MigrationName'..." -ForegroundColor Yellow
        & dotnet ef migrations add $MigrationName --project $projectPath --output-dir $migrationsDir
        Write-Host "[SUCCESS] Migration created in $migrationsDir" -ForegroundColor Green
    }

    "script" {
        $outFile = Join-Path $serviceDir "src\Infrastructure\Persistence\Migrations\idempotent_migration.sql"
        Write-Host "Generating idempotent SQL migration script..." -ForegroundColor Yellow
        & dotnet ef migrations script --idempotent --project $projectPath --output $outFile
        Write-Host "[SUCCESS] SQL Script written to $outFile" -ForegroundColor Green
    }

    "bundle" {
        $outFile = Join-Path $serviceDir "src\Infrastructure\Persistence\migration_bundle"
        Write-Host "Generating self-contained executable migration bundle..." -ForegroundColor Yellow
        & dotnet ef migrations bundle --project $projectPath --output $outFile --force
        Write-Host "[SUCCESS] Migration bundle created at $outFile" -ForegroundColor Green
    }

    "apply" {
        Write-Host "Applying pending migrations to target PostgreSQL database..." -ForegroundColor Yellow
        & dotnet ef database update --project $projectPath
        Write-Host "[SUCCESS] Database update complete." -ForegroundColor Green
    }

    "list" {
        Write-Host "Listing applied and pending migrations..." -ForegroundColor Yellow
        & dotnet ef migrations list --project $projectPath
    }
}
