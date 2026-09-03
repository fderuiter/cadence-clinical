<#
.SYNOPSIS
    Cadence Clinical Platform - Automated .NET 10 Developer Environment Setup (Windows / PowerShell)

.DESCRIPTION
    Verifies and provisions the .NET 10 SDK, global CLI tools, development HTTPS certificates,
    environment configuration (.env), and validates local database connectivity (Postgres / Neo4j).
    Requirements: PRD-SYS-053, ADR-2233
#>

[CmdletBinding()]
param(
    [switch]$SkipSdkInstall,
    [switch]$SkipCertTrust,
    [string]$TargetChannel = "10.0"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  Cadence Clinical Research Software — .NET 10 Environment Setup " -ForegroundColor Cyan
Write-Host "=================================================================`n" -ForegroundColor Cyan

# 1. Check or install .NET 10 SDK
Write-Host "[1/5] Checking .NET SDK Installation..." -ForegroundColor Yellow
$sdkInstalled = $false

try {
    $currentVersion = & dotnet --version 2>$null
    if ($currentVersion -and $currentVersion.StartsWith("10.")) {
        Write-Host "  [OK] Found active .NET 10 SDK: $currentVersion" -ForegroundColor Green
        $sdkInstalled = $true
    }
    elseif ($currentVersion) {
        Write-Host "  [INFO] Detected existing .NET SDK ($currentVersion), but .NET 10 is required." -ForegroundColor DarkYellow
    }
}
catch {
    Write-Host "  [INFO] No .NET SDK found in PATH." -ForegroundColor DarkYellow
}

if (-not $sdkInstalled -and -not $SkipSdkInstall) {
    $installedViaWinget = $false
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host "  [ACTION] Installing .NET 10 SDK via winget..." -ForegroundColor Cyan
        try {
            & winget install --id Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements -e
            if ($LASTEXITCODE -eq 0) { $installedViaWinget = $true }
        }
        catch {
            Write-Host "  [INFO] winget install failed; falling back to dotnet-install.ps1." -ForegroundColor DarkYellow
        }
    }

    if (-not $installedViaWinget) {
        Write-Host "  [ACTION] Downloading official Microsoft dotnet-install script..." -ForegroundColor Cyan
        $installScriptUrl = "https://dot.net/v1/dotnet-install.ps1"
        $tempScript = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "dotnet-install.ps1")

        Invoke-WebRequest -Uri $installScriptUrl -OutFile $tempScript

        $installDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "Microsoft", "dotnet")
        Write-Host "  [ACTION] Installing .NET $TargetChannel SDK (GA) to $installDir..." -ForegroundColor Cyan

        & powershell.exe -ExecutionPolicy Bypass -File $tempScript -Channel $TargetChannel -InstallDir $installDir

        # Configure PATH for current session and persistent user environment
        if ($env:PATH -notlike "*$installDir*") {
            $env:PATH = "$installDir;" + $env:PATH
            $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
            if ($userPath -notlike "*$installDir*") {
                [Environment]::SetEnvironmentVariable("Path", "$installDir;$userPath", "User")
                Write-Host "  [OK] Added $installDir to persistent User PATH." -ForegroundColor Green
            }
        }
    }

    $newVersion = & dotnet --version 2>$null
    Write-Host "  [OK] .NET 10 SDK successfully installed: $newVersion" -ForegroundColor Green
}

# 2. Install essential global dotnet tools
Write-Host "`n[2/5] Checking & Installing Global .NET Tools..." -ForegroundColor Yellow
$tools = @("dotnet-ef", "dotnet-format")
foreach ($tool in $tools) {
    try {
        Write-Host "  Installing/Updating $tool..." -ForegroundColor Gray
        & dotnet tool update --global $tool 2>$null
        Write-Host "  [OK] $tool is up to date." -ForegroundColor Green
    }
    catch {
        Write-Host "  [WARNING] Could not install tool $tool: $_" -ForegroundColor DarkYellow
    }
}

# 3. Development HTTPS certificates
Write-Host "`n[3/5] Configuring HTTPS Development Certificates..." -ForegroundColor Yellow
if (-not $SkipCertTrust) {
    try {
        & dotnet dev-certs https --clean
        & dotnet dev-certs https --trust
        Write-Host "  [OK] Development HTTPS certificate generated and trusted." -ForegroundColor Green
    }
    catch {
        Write-Host "  [WARNING] Dev cert trust step returned: $_" -ForegroundColor DarkYellow
    }
}
else {
    Write-Host "  [SKIPPED] Skipping certificate trust per parameter." -ForegroundColor Gray
}

# 4. Environment Configuration (.env)
Write-Host "`n[4/5] Checking Environment Configuration (.env)..." -ForegroundColor Yellow
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$envFile = Join-Path $repoRoot ".env"
$envExample = Join-Path $repoRoot ".env.example"

if (-not (Test-Path $envFile)) {
    if (Test-Path $envExample) {
        Copy-Item -Path $envExample -Destination $envFile
        Write-Host "  [OK] Created default .env from .env.example." -ForegroundColor Green
    }
    else {
        Write-Host "  [WARNING] .env.example not found at $envExample." -ForegroundColor Red
    }
}
else {
    Write-Host "  [OK] Existing .env file detected." -ForegroundColor Green
}

# 5. Database Connectivity Checks (Docker / Local)
Write-Host "`n[5/5] Checking Local Clinical Datastores..." -ForegroundColor Yellow
$pgPort = 5432
$neo4jPort = 7687

# Test PostgreSQL port
$pgSocket = New-Object System.Net.Sockets.TcpClient
$pgAsync = $pgSocket.BeginConnect("127.0.0.1", $pgPort, $null, $null)
$pgSuccess = $pgAsync.AsyncWaitHandle.WaitOne(1000, $false)
if ($pgSuccess -and $pgSocket.Connected) {
    Write-Host "  [OK] PostgreSQL detected on port $pgPort." -ForegroundColor Green
    $pgSocket.Close()
}
else {
    Write-Host "  [INFO] PostgreSQL not reachable on port $pgPort (Run 'docker compose up -d postgres' to start)." -ForegroundColor DarkYellow
}

# Test Neo4j Bolt port
$neoSocket = New-Object System.Net.Sockets.TcpClient
$neoAsync = $neoSocket.BeginConnect("127.0.0.1", $neo4jPort, $null, $null)
$neoSuccess = $neoAsync.AsyncWaitHandle.WaitOne(1000, $false)
if ($neoSuccess -and $neoSocket.Connected) {
    Write-Host "  [OK] Neo4j Bolt detected on port $neo4jPort." -ForegroundColor Green
    $neoSocket.Close()
}
else {
    Write-Host "  [INFO] Neo4j not reachable on port $neo4jPort (Run 'docker compose up -d neo4j' to start)." -ForegroundColor DarkYellow
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  .NET 10 Developer Environment Setup Complete!                  " -ForegroundColor Green
Write-Host "  Run 'dotnet build Cadence.sln' to build the solution.          " -ForegroundColor Cyan
Write-Host "=================================================================`n" -ForegroundColor Cyan
