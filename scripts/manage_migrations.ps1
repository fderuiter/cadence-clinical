<#
.SYNOPSIS
    Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (PowerShell)

.DESCRIPTION
    Automates generating, scripting, verifying, listing, bundling, and applying idempotent Entity Framework Core 10
    database migrations across all 18 Cadence persistence modules under 21 CFR Part 11 requirements.
    Requirements: PRD-SYS-053, ADR-2233
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
$cliProject = Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj"

switch ($Action) {
    "add" {
        if ([string]::IsNullOrWhiteSpace($Service) -or $Service.ToLowerInvariant() -eq "all" -or [string]::IsNullOrWhiteSpace($MigrationName)) {
            Write-Host "Error: Specific module name and MigrationName are required for 'add'." -ForegroundColor Red
            exit 1
        }
        Invoke-Checked { & dotnet run --project $cliProject -- migrations add $Service $MigrationName }
        exit 0
    }
    "script" {
        $target = if ([string]::IsNullOrWhiteSpace($Service)) { "all" } else { $Service }
        Invoke-Checked { & dotnet run --project $cliProject -- migrations script $target }
        exit 0
    }
    "list" {
        $target = if ([string]::IsNullOrWhiteSpace($Service)) { "all" } else { $Service }
        Invoke-Checked { & dotnet run --project $cliProject -- migrations list $target }
        exit 0
    }
    "verify" {
        $target = if ([string]::IsNullOrWhiteSpace($Service)) { "all" } else { $Service }
        Invoke-Checked { & dotnet run --project $cliProject -- migrations verify $target }
        exit 0
    }
}

$knownServices = @(
    "ctms", "econsent", "eisf", "etmf", "execution", "executionmodule", "fileshare", "imaging",
    "intelligence", "interop", "kernel", "knowledge", "notifications", "org", "payments",
    "quality", "safety", "tickets"
)

$targetServices = @()
if ($Service.ToLowerInvariant() -eq "all") {
    $targetServices = $knownServices
} else {
    $targetServices = @($Service.ToLowerInvariant())
}

foreach ($svc in $targetServices) {
    $capName = Capitalize-Name $svc
    $projectDir = Join-Path $repoRoot "apps\$svc\src"
    $projectPath = Join-Path $projectDir "Cadence.Apps.$capName.csproj"
    $compiledNs = "Cadence.Apps.$capName.CompiledModels"

    switch ($svc) {
        "kernel" {
            $projectDir = Join-Path $repoRoot "packages\kernel\src\Cadence.Kernel.Persistence"
            $projectPath = Join-Path $projectDir "Cadence.Kernel.Persistence.csproj"
            $compiledNs = "Cadence.Kernel.Persistence.CompiledModels"
        }
        "intelligence" {
            $projectDir = Join-Path $repoRoot "apps\intelligence\src"
            $projectPath = Join-Path $projectDir "Cadence.Modules.Intelligence.csproj"
            $compiledNs = "Cadence.Modules.Intelligence.CompiledModels"
        }
        { $_ -in @("executionmodule", "execution-module", "execution_module") } {
            $projectDir = Join-Path $repoRoot "apps\execution\module"
            $projectPath = Join-Path $projectDir "Cadence.Modules.Execution.csproj"
            $compiledNs = "Cadence.Modules.Execution.CompiledModels"
        }
    }

    if (-not (Test-Path $projectPath)) {
        Write-Host "Error: Project not found at $projectPath" -ForegroundColor Red
        exit 1
    }

    Write-Host "`n=== Cadence EF Core Migration Manager ===" -ForegroundColor Cyan
    Write-Host "Module:  $svc ($projectPath)" -ForegroundColor Gray
    Write-Host "Action:  $Action`n" -ForegroundColor Gray

    switch ($Action) {
        "bundle" {
            $bundleDir = Join-Path $repoRoot "TestResults\migrations"
            New-Item -ItemType Directory -Force -Path $bundleDir | Out-Null
            $outFile = Join-Path $bundleDir "${svc}_migration_bundle"
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

        "optimize" {
            Write-Host "Generating pre-compiled EF Core models for target module..." -ForegroundColor Yellow
            Invoke-Checked { & dotnet ef dbcontext optimize --project $projectPath --output-dir "CompiledModels" --namespace $compiledNs }
            Write-Host "[SUCCESS] Compiled model optimization complete." -ForegroundColor Green
        }
    }
}
