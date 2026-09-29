<#
.SYNOPSIS
    Cadence Clinical Platform - Interactive Microservice Migration & Scaffolding Wizard (PowerShell)

.DESCRIPTION
    Interactively guides engineers and agents through scaffolding a new .NET 10 microservice
    using Single-Assembly Clean Architecture, GxP audit interceptors, and Result monads.
    Requirements: PRD-SYS-053, ADR-2233, ADR-2428
#>

[CmdletBinding()]
param(
    [string]$ServiceName
)

$ErrorActionPreference = "Stop"

function Capitalize-Name([string]$name) {
    return (Get-Culture).TextInfo.ToTitleCase($name.ToLowerInvariant())
}

Clear-Host
Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   CADENCE CLINICAL PLATFORM — MICROSERVICE TRANSCRIPTION WIZARD                " -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Architecture: Single-Assembly Clean Architecture (.NET 10 / C# 14)" -ForegroundColor Gray
Write-Host "  GxP Standard: 21 CFR Part 11 / Merkle Interceptor (PRD-SYS-053)" -ForegroundColor Gray
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkCyan

if ([string]::IsNullOrWhiteSpace($ServiceName)) {
    Write-Host "Available Clinical Services:" -ForegroundColor Yellow
    Write-Host "  [1] execution      (EDC Clinical Observation Engine)"
    Write-Host "  [2] designer       (MDR Protocol Designer - Neo4j)"
    Write-Host "  [3] ctms           (Clinical Trial Management System)"
    Write-Host "  [4] etmf           (Electronic Trial Master File)"
    Write-Host "  [5] eisf           (Investigator Site File)"
    Write-Host "  [6] quality        (CAPA & Deviation Tracking)"
    Write-Host "  [7] safety         (SUSAR & Safety Reporting)"
    Write-Host "  [8] interop        (FHIR & CDISC ODM Connectors)"
    Write-Host "  [9] notifications  (Clinical Notification Dispatch)"
    Write-Host "  [10] econsent      (Electronic Consent Pipeline)`n"

    $selection = Read-Host "Enter service name or number (e.g. 'ctms' or '3')"
    switch ($selection) {
        "1" { $ServiceName = "execution" }
        "2" { $ServiceName = "designer" }
        "3" { $ServiceName = "ctms" }
        "4" { $ServiceName = "etmf" }
        "5" { $ServiceName = "eisf" }
        "6" { $ServiceName = "quality" }
        "7" { $ServiceName = "safety" }
        "8" { $ServiceName = "interop" }
        "9" { $ServiceName = "notifications" }
        "10" { $ServiceName = "econsent" }
        default { $ServiceName = $selection.Trim().ToLowerInvariant() }
    }
}

if ([string]::IsNullOrWhiteSpace($ServiceName)) {
    Write-Host "Error: Service name is required." -ForegroundColor Red
    exit 1
}

$capName = Capitalize-Name $ServiceName
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$serviceDir = Join-Path $repoRoot "apps\$ServiceName"
$srcDir = Join-Path $serviceDir "src"
$domainDir = Join-Path $srcDir "Domain"
$appDir = Join-Path $srcDir "Application"
$infraDir = Join-Path $srcDir "Infrastructure"
$endpointsDir = Join-Path $srcDir "Endpoints"
$csprojPath = Join-Path $srcDir "Cadence.Apps.$capName.csproj"

Write-Host "`nTarget Service: Cadence.Apps.$capName" -ForegroundColor Cyan
Write-Host "Source Path:    $srcDir" -ForegroundColor Gray

# Step 1: Create Clean Architecture Directory Layout
Write-Host "`n[1/5] Creating Single-Assembly Clean Architecture Layout..." -ForegroundColor Yellow
$dirs = @($srcDir, $domainDir, $appDir, $infraDir, $endpointsDir)
foreach ($dir in $dirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $([System.IO.Path]::GetRelativePath($repoRoot, $dir))" -ForegroundColor Green
    }
}

# Step 2: Create .csproj if missing
Write-Host "`n[2/5] Scaffolding Project Configuration..." -ForegroundColor Yellow
if (-not (Test-Path $csprojPath)) {
    $csprojContent = @"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>Cadence.Apps.$capName</RootNamespace>
    <Description>Cadence Clinical $capName Service (.NET 10 / C# 14 Minimal APIs).</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\packages\engine_core\src\Cadence.Core.Domain.csproj" />
    <ProjectReference Include="..\..\..\packages\security\src\Cadence.Security.csproj" />
    <ProjectReference Include="..\..\..\packages\database\src\Cadence.Database.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Scalar.AspNetCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="FluentValidation" />
  </ItemGroup>
</Project>
"@
    Set-Content -Path $csprojPath -Value $csprojContent.Trim() -Encoding utf8
    Write-Host "  Created: $([System.IO.Path]::GetRelativePath($repoRoot, $csprojPath))" -ForegroundColor Green
} else {
    Write-Host "  Project file exists: $([System.IO.Path]::GetRelativePath($repoRoot, $csprojPath))" -ForegroundColor Gray
}

# Step 3: Scaffold Clean Architecture Boilerplate
Write-Host "`n[3/5] Generating Clean Architecture Starter Boilerplate..." -ForegroundColor Yellow

# Domain Aggregate
$domainFile = Join-Path $domainDir "$($capName)Record.cs"
if (-not (Test-Path $domainFile)) {
    $domainCode = @"
using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.$capName.Domain;

/// <summary>
/// Domain aggregate root representing a core clinical entity in the $capName domain.
/// @req:PRD-SYS-053
/// </summary>
public sealed class $($capName)Record : AggregateRoot<string>
{
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = "ACTIVE";

    private $($capName)Record() : base() { }

    public static Result<$($capName)Record> Create(string title, string createdBy, string reason)
    {
        if (string.IsNullOrWhiteSpace(title))
            return DomainError.Validation("ERR_TITLE_REQUIRED", "Title is required.");

        var record = new $($capName)Record
        {
            Id = Guid.NewGuid().ToString("D"),
            Title = title,
            CreatedBy = createdBy,
            ReasonForChange = reason,
            VersionIndex = 1
        };
        return record;
    }
}
"@
    Set-Content -Path $domainFile -Value $domainCode.Trim() -Encoding utf8
    Write-Host "  Created Domain Entity: $([System.IO.Path]::GetFileName($domainFile))" -ForegroundColor Green
}

# Application Service Interface
$appFile = Join-Path $appDir "I$($capName)ApplicationService.cs"
if (-not (Test-Path $appFile)) {
    $appCode = @"
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.$capName.Domain;
using Cadence.Core.Domain;

namespace Cadence.Apps.$capName.Application;

public record Create$($capName)Command(string Title, string User, string Reason);
public record $($capName)Dto(string Id, string Title, string Status, long VersionIndex);

/// <summary>
/// Application service port for $capName use cases.
/// @req:PRD-SYS-053
/// </summary>
public interface I$($capName)ApplicationService
{
    Task<Result<$($capName)Dto>> CreateAsync(Create$($capName)Command command, CancellationToken ct = default);
}
"@
    Set-Content -Path $appFile -Value $appCode.Trim() -Encoding utf8
    Write-Host "  Created Application Port: $([System.IO.Path]::GetFileName($appFile))" -ForegroundColor Green
}

# Minimal API Endpoints
$endpointsFile = Join-Path $endpointsDir "$($capName)Endpoints.cs"
if (-not (Test-Path $endpointsFile)) {
    $endpointsCode = @"
using System.Threading;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Cadence.Apps.$capName.Application;

namespace Cadence.Apps.$capName.Endpoints;

public static class $($capName)Endpoints
{
    public static RouteGroupBuilder Map$($capName)Endpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (Create$($capName)Command cmd, I$($capName)ApplicationService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(cmd, ct);
            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error!.Description);
        })
        .WithName("Create$($capName)")
        .WithSummary("Registers a new record in $capName service.");

        return group;
    }
}
"@
    Set-Content -Path $endpointsFile -Value $endpointsCode.Trim() -Encoding utf8
    Write-Host "  Created Endpoints: $([System.IO.Path]::GetFileName($endpointsFile))" -ForegroundColor Green
}

# Step 4: Check Solution Binding
Write-Host "`n[4/5] Checking Cadence.sln Solution Binding..." -ForegroundColor Yellow
$slnPath = Join-Path $repoRoot "Cadence.sln"
$slnContent = Get-Content $slnPath -Raw
if ($slnContent -like "*Cadence.Apps.$capName.csproj*") {
    Write-Host "  [SUCCESS] Project is already bound in Cadence.sln." -ForegroundColor Green
} else {
    Write-Host "  [ACTION NEEDED] Project needs to be bound in Cadence.sln." -ForegroundColor Yellow
    Write-Host "  Run 'dotnet sln Cadence.sln add apps\$ServiceName\src\Cadence.Apps.$capName.csproj --solution-folder Apps'" -ForegroundColor Cyan
}

# Step 5: ADR Supersession Verification
Write-Host "`n[5/5] Checking Architectural Decision Records (ADRs)..." -ForegroundColor Yellow
$adrCheck = Read-Host "Does this migration supersede any specific legacy Python ADR? (y/N)"
if ($adrCheck -eq 'y' -or $adrCheck -eq 'Y') {
    $legacyAdr = Read-Host "Enter the legacy ADR filename (e.g. 2026-08-28-something.md)"
    Write-Host "Remember to update the legacy ADR status to 'Superseded' and update docs/adr/index.md per docs/MIGRATION_WORKFLOW.md." -ForegroundColor Cyan
}

Write-Host "Running ADR validation..." -ForegroundColor Gray
& dotnet run --project (Join-Path $repoRoot "packages\cli\src\Cadence.Cli.csproj") -- check adrs

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   SCAFFOLDING COMPLETE FOR Cadence.Apps.$capName                               " -ForegroundColor Green
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Next Steps:" -ForegroundColor White
Write-Host "    1. Implement persistence DbContext in Infrastructure/" -ForegroundColor Cyan
Write-Host "    2. Implement I$($capName)ApplicationService in Application/" -ForegroundColor Cyan
Write-Host "    3. Add xUnit tests under tests/ with [GxPRequirement(`"PRD-SYS-053`")]" -ForegroundColor Cyan
Write-Host "    4. Run 'dotnet cadence gxp sync' to verify RTM compliance" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor DarkCyan

