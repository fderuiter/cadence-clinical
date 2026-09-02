<#
.SYNOPSIS
    Cadence Clinical Platform - Interactive Service Decommissioning Wizard (PowerShell)

.DESCRIPTION
    Safely guides developers and agents through decommissioning legacy Python service code
    after .NET 10 transcription, verifying behavioral parity, gateway cutover, and GxP compliance.
    Requirements: PRD-SYS-053, ADR-2233
#>

[CmdletBinding()]
param(
    [string]$ServiceName
)

$ErrorActionPreference = "Stop"

Clear-Host
Write-Host "`n================================================================================" -ForegroundColor Red
Write-Host "   CADENCE CLINICAL PLATFORM — SERVICE DECOMMISSIONING WIZARD                  " -ForegroundColor Red
Write-Host "================================================================================" -ForegroundColor Red
Write-Host "  Process: Decommissioning Legacy Python Implementations (`"Clearing Out the Old`")" -ForegroundColor Gray
Write-Host "  Regulatory Standard: 21 CFR Part 11 / Change Control (PRD-SYS-053)" -ForegroundColor Gray
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkRed

if ([string]::IsNullOrWhiteSpace($ServiceName)) {
    $ServiceName = Read-Host "Enter the name of the service to decommission (e.g. 'execution')"
}

if ([string]::IsNullOrWhiteSpace($ServiceName)) {
    Write-Host "Error: Service name is required." -ForegroundColor Red
    exit 1
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$serviceDir = Join-Path $repoRoot "apps\$ServiceName"
$csharpCsproj = Join-Path $serviceDir "src\Cadence.Apps.*.csproj"
$pySrcDir = Join-Path $serviceDir "src\cadence\$ServiceName"
$pyprojectPath = Join-Path $repoRoot "pyproject.toml"

# Step 1: Verify .NET 10 Implementation Exists
Write-Host "[1/5] Verifying .NET 10 Replacement..." -ForegroundColor Yellow
$foundCsproj = Get-ChildItem -Path (Join-Path $serviceDir "src") -Filter "*.csproj" -ErrorAction SilentlyContinue
if (-not $foundCsproj) {
    Write-Host "  [ABORT] No .NET 10 .csproj found in $serviceDir\src. Scaffolding must precede decommissioning!" -ForegroundColor Red
    exit 1
}
Write-Host "  [OK] Found active .NET 10 service: $($foundCsproj.Name)" -ForegroundColor Green

# Step 2: Behavioral Parity & GxP Verification Confirmation
Write-Host "`n[2/5] Behavioral Parity & Contract Confirmation..." -ForegroundColor Yellow
Write-Host "  Before decommissioning legacy Python code, confirm the following:" -ForegroundColor Gray
Write-Host "    1. All .NET 10 unit and integration tests pass with 100% GxP requirement traceability."
Write-Host "    2. Minimal API OpenAPI contracts (/openapi/v1.json) match or supersede legacy schemas."
Write-Host "    3. Gateway routing in apps/gateway/ points to the active .NET 10 port.`n"

$parityConfirm = Read-Host "Have all parity and regulatory requirements been validated? (y/N)"
if ($parityConfirm -ne 'y' -and $parityConfirm -ne 'Y') {
    Write-Host "Decommissioning aborted. Please complete test verification first." -ForegroundColor DarkYellow
    exit 0
}

# Step 3: Gateway Cutover Check
Write-Host "`n[3/5] Gateway Cutover Routing..." -ForegroundColor Yellow
Write-Host "  Please verify apps/gateway is routing traffic to the .NET 10 service." -ForegroundColor Gray
$gatewayConfirm = Read-Host "Is gateway reverse proxy cutover confirmed? (y/N)"
if ($gatewayConfirm -ne 'y' -and $gatewayConfirm -ne 'Y') {
    Write-Host "Decommissioning paused until gateway cutover is completed." -ForegroundColor DarkYellow
    exit 0
}

# Step 4: Python Source Purge
Write-Host "`n[4/5] Legacy Python Source Pruning..." -ForegroundColor Yellow
Write-Host "  The following legacy Python directory will be pruned:" -ForegroundColor DarkYellow
Write-Host "    $pySrcDir" -ForegroundColor Red

if (Test-Path $pySrcDir) {
    $purgeConfirm = Read-Host "`nType 'DELETE' to confirm permanent removal of legacy Python code"
    if ($purgeConfirm -eq 'DELETE') {
        Remove-Item -Path $pySrcDir -Recurse -Force
        Write-Host "  [SUCCESS] Pruned legacy Python source directory." -ForegroundColor Green
    }
    else {
        Write-Host "  [SKIPPED] Legacy Python directory was not deleted." -ForegroundColor Gray
    }
}
else {
    Write-Host "  Legacy Python directory is already clean." -ForegroundColor Green
}

# Step 5: Unlink Workspace Member in pyproject.toml
Write-Host "`n[5/5] Unlinking pyproject.toml Workspace Member..." -ForegroundColor Yellow
if (Test-Path $pyprojectPath) {
    $pyprojectContent = Get-Content $pyprojectPath -Raw
    $memberEntry = "apps/$ServiceName"
    if ($pyprojectContent -like "*$memberEntry*") {
        Write-Host "  Found '$memberEntry' in pyproject.toml. Removing workspace entry..." -ForegroundColor Gray
        $updatedPyproject = $pyprojectContent.Replace("    `"$memberEntry`",`n", "")
        Set-Content -Path $pyprojectPath -Value $updatedPyproject -Encoding utf8
        Write-Host "  [SUCCESS] Unlinked '$memberEntry' from uv workspace." -ForegroundColor Green
    }
    else {
        Write-Host "  Service '$memberEntry' is already unlinked from pyproject.toml." -ForegroundColor Green
    }
}

Write-Host "`nRunning ADR and GxP synchronization..." -ForegroundColor Gray
& python (Join-Path $repoRoot "scripts\validate_adrs.py")
& python (Join-Path $repoRoot "scripts\sync_gxp.py")

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   DECOMMISSIONING COMPLETE FOR $ServiceName                                    " -ForegroundColor Green
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  The service is now running purely on .NET 10!" -ForegroundColor White
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkCyan

