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
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("add", "script", "bundle", "apply", "seed", "list")]
    [string]$Action,

    [Parameter(Position = 1, Mandatory = $true)]
    [string]$Service,

    [Parameter(Position = 2)]
    [string]$MigrationName
)

$ErrorActionPreference = "Stop"

function Invoke-Checked([scriptblock]$Command) {
    # $ErrorActionPreference does not trap native exit codes; fail the script instead of printing [SUCCESS].
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: '$Command' exited with code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

function Capitalize-Name([string]$name) {
    return (Get-Culture).TextInfo.ToTitleCase($name.ToLowerInvariant())
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$knownServices = @("ctms", "econsent", "eisf", "etmf", "execution", "fileshare", "imaging", "interop", "knowledge", "notifications", "org", "payments", "quality", "safety", "tickets")

$targetServices = @()
if ($Service.ToLowerInvariant() -eq "all") {
    $targetServices = $knownServices
} else {
    $targetServices = @($Service)
}

foreach ($svc in $targetServices) {
    $capName = Capitalize-Name $svc
    $projectDir = Join-Path $repoRoot "apps\$svc\src"
    $projectPath = Join-Path $projectDir "Cadence.Apps.$capName.csproj"
    $migrationsDir = "Infrastructure\Persistence\Migrations"

    # The Clinical Kernel keeps its migrations in Kernel.Persistence (ADR-2247, spec §2.4).
    if ($svc -eq "kernel") {
        $projectDir = Join-Path $repoRoot "packages\kernel\src\Cadence.Kernel.Persistence"
        $projectPath = Join-Path $projectDir "Cadence.Kernel.Persistence.csproj"
        $migrationsDir = "Migrations"
    }

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
            Invoke-Checked { & dotnet ef migrations add $MigrationName --project $projectPath --output-dir $migrationsDir }
            Write-Host "[SUCCESS] Migration created in $migrationsDir" -ForegroundColor Green
        }

        "script" {
            $outFile = Join-Path $projectDir "$migrationsDir\idempotent_migration.sql"
            Write-Host "Generating idempotent SQL migration script..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet ef migrations script --idempotent --project $projectPath --output $outFile }
            Write-Host "[SUCCESS] SQL Script written to $outFile" -ForegroundColor Green
        }

        "bundle" {
            $outFile = Join-Path $projectDir "migration_bundle"
            Write-Host "Generating self-contained executable migration bundle..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet ef migrations bundle --project $projectPath --output $outFile --force }
            Write-Host "[SUCCESS] Migration bundle created at $outFile" -ForegroundColor Green
        }

        "apply" {
            Write-Host "Applying pending migrations to target PostgreSQL database..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet ef database update --project $projectPath }
            Write-Host "[SUCCESS] Database update complete." -ForegroundColor Green
        }

        "seed" {
            Write-Host "Executing C# DbContext seeders for target service..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet run --project $projectPath --no-build -- --seed }
            Write-Host "[SUCCESS] Seeding complete." -ForegroundColor Green
        }

        "list" {
            Write-Host "Listing applied and pending migrations..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet ef migrations list --project $projectPath }
        }
    }
}
