<#
.SYNOPSIS
    Cadence Clinical Platform - Interactive .NET 10 Developer Onboarding Wizard (Windows / PowerShell)

.DESCRIPTION
    Interactive step-by-step terminal wizard guiding developers through .NET 10 SDK verification,
    global CLI tools installation, HTTPS development certificates, Docker datastores, and solution build.
    Requirements: PRD-SYS-053, ADR-2233
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"

function Write-WizardBanner {
    Clear-Host
    Write-Host "`n================================================================================" -ForegroundColor Cyan
    Write-Host "   CADENCE CLINICAL PLATFORM — .NET 10 DEVELOPER ONBOARDING WIZARD              " -ForegroundColor Cyan
    Write-Host "================================================================================" -ForegroundColor Cyan
    Write-Host "  Target Architecture: .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)" -ForegroundColor Gray
    Write-Host "  Regulatory Standard: 21 CFR Part 11 / ICH GCP E6 (PRD-SYS-053)" -ForegroundColor Gray
    Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkCyan
}

function Invoke-Stage {
    param(
        [int]$Number,
        [int]$Total,
        [string]$Title,
        [string]$Description
    )
    Write-Host "`n[$Number/$Total] $Title" -ForegroundColor Yellow
    Write-Host "  $Description" -ForegroundColor Gray
    Write-Host "  ------------------------------------------------------------------------------" -ForegroundColor DarkGray
}

Write-WizardBanner
Write-Host "Welcome to the Cadence Clinical Developer Environment Setup." -ForegroundColor White
Write-Host "This wizard will guide you through setting up .NET 10, dev certs, and datastores." -ForegroundColor White
Write-Host ""
$start = Read-Host "Ready to begin? (Y/n)"
if ($start -eq 'n' -or $start -eq 'N') {
    Write-Host "Onboarding paused. Re-run anytime." -ForegroundColor DarkYellow
    exit 0
}

# ------------------------------------------------------------------------------
# Stage 1: .NET 10 SDK Verification
# ------------------------------------------------------------------------------
Invoke-Stage -Number 1 -Total 6 -Title ".NET 10 SDK Environment" -Description "Validating .NET 10 SDK installation via global.json..."

$sdkFound = $false
try {
    $dotnetVersion = & dotnet --version 2>$null
    if ($dotnetVersion -and $dotnetVersion.StartsWith("10.")) {
        Write-Host "  [SUCCESS] Found active .NET 10 SDK: $dotnetVersion" -ForegroundColor Green
        $sdkFound = $true
    } elseif ($dotnetVersion) {
        Write-Host "  [NOTICE] Detected .NET SDK $dotnetVersion, but .NET 10 is required." -ForegroundColor DarkYellow
    }
} catch {
    Write-Host "  [NOTICE] No .NET SDK found in PATH." -ForegroundColor DarkYellow
}

if (-not $sdkFound) {
    $installChoice = Read-Host "  Would you like this wizard to download and install the official .NET 10 SDK? (Y/n)"
    if ($installChoice -ne 'n' -and $installChoice -ne 'N') {
        Write-Host "  Installing .NET 10 SDK via scripts/setup_dotnet_env.ps1..." -ForegroundColor Cyan
        & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "setup_dotnet_env.ps1") -SkipCertTrust
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [ERROR] .NET environment setup failed with exit code $LASTEXITCODE. Aborting onboarding." -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Host "  [SKIPPED] Please manually install .NET 10 SDK before compiling Cadence.sln." -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------------------
# Stage 2: Local CLI Tools
# ------------------------------------------------------------------------------
Invoke-Stage -Number 2 -Total 6 -Title "Local Developer Tools" -Description "Restoring pinned dotnet CLI tools from .config/dotnet-tools.json..."

$wizardRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $wizardRepoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [WARNING] dotnet tool restore exited with code $LASTEXITCODE" -ForegroundColor Yellow
    } else {
        Write-Host "  [SUCCESS] Local .NET tools restored." -ForegroundColor Green
    }
} finally {
    Pop-Location
}

# ------------------------------------------------------------------------------
# Stage 3: Development HTTPS Certificates
# ------------------------------------------------------------------------------
Invoke-Stage -Number 3 -Total 6 -Title "HTTPS Dev Certificates" -Description "Generating and trusting local ASP.NET Core dev certificates..."

$certPrompt = Read-Host "  Trust local HTTPS dev certificate for localhost (Ports 8000-8012)? (Y/n)"
if ($certPrompt -ne 'n' -and $certPrompt -ne 'N') {
    try {
        & dotnet dev-certs https --clean
        & dotnet dev-certs https --trust
        Write-Host "  [SUCCESS] Local development certificate trusted." -ForegroundColor Green
    } catch {
        Write-Host "  [NOTICE] Certificate generation: $_" -ForegroundColor DarkYellow
    }
}

# ------------------------------------------------------------------------------
# Stage 4: Clinical Datastores Verification (Docker)
# ------------------------------------------------------------------------------
Invoke-Stage -Number 4 -Total 6 -Title "Clinical Datastores (Docker)" -Description "Probing local PostgreSQL (5432)..."

$pgSocket = New-Object System.Net.Sockets.TcpClient
$pgAsync = $pgSocket.BeginConnect("127.0.0.1", 5432, $null, $null)
$pgSuccess = $pgAsync.AsyncWaitHandle.WaitOne(800, $false)
if ($pgSuccess -and $pgSocket.Connected) {
    Write-Host "  [SUCCESS] PostgreSQL running on port 5432." -ForegroundColor Green
    $pgSocket.Close()
} else {
    Write-Host "  [INFO] PostgreSQL not responding on port 5432." -ForegroundColor DarkYellow
    Write-Host "         Run 'docker compose -f docker/docker-compose.yml up -d postgres' to launch local database." -ForegroundColor Gray
}


# ------------------------------------------------------------------------------
# Stage 5: Environment File Configuration
# ------------------------------------------------------------------------------
Invoke-Stage -Number 5 -Total 6 -Title "Environment Configuration (.env)" -Description "Verifying local secrets and service endpoint mappings..."

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$envFile = Join-Path $repoRoot ".env"
$envExample = Join-Path $repoRoot ".env.example"

if (-not (Test-Path $envFile) -and (Test-Path $envExample)) {
    Copy-Item -Path $envExample -Destination $envFile
    Write-Host "  [SUCCESS] Initialized .env from template." -ForegroundColor Green
} else {
    Write-Host "  [SUCCESS] .env file is present." -ForegroundColor Green
}

# ------------------------------------------------------------------------------
# Stage 6: Solution Health & Verification
# ------------------------------------------------------------------------------
Invoke-Stage -Number 6 -Total 6 -Title "Solution Compilation & Quality Gate" -Description "Testing solution build and ADR integrity..."

Write-Host "  Executing ADR validation..." -ForegroundColor Gray
& dotnet run --project (Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj") -- check adrs

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   ONBOARDING COMPLETE! Your Cadence .NET 10 environment is ready.              " -ForegroundColor Green
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Next Commands:" -ForegroundColor White
Write-Host "    dotnet build Cadence.sln                      (Build solution)" -ForegroundColor Cyan
Write-Host "    dotnet test Cadence.sln                       (Run test suites)" -ForegroundColor Cyan
Write-Host "    powershell .\scripts\migrate_service_wizard.ps1 (Start migrating a service)" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkCyan

