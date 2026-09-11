param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$apiTestsProject = Join-Path $repoRoot 'tests\RJ.ApiTests\RJ.ApiTests.csproj'
$integrationProject = Join-Path $repoRoot 'tests\RJ.IntegrationTests\RJ.IntegrationTests.csproj'
$apiProject = Join-Path $repoRoot 'src\RJ.Api\RJ.Api.csproj'
$waveDScript = Join-Path $scriptRoot 'test-rjudi-wave-d.ps1'

function Invoke-GateStep {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [scriptblock] $Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Push-Location $repoRoot
try {
    Invoke-GateStep 'build RJ.Api' { dotnet build $apiProject --no-restore }
    Invoke-GateStep 'maintenance planner and retention tests' {
        dotnet test $domainProject --no-restore --filter 'FullyQualifiedName~ProcessSummaryMaintenanceTests'
    }
    Invoke-GateStep 'maintenance scheduler configuration tests' {
        dotnet test $apiTestsProject --no-restore --filter 'FullyQualifiedName~ProcessSummaryMaintenanceOptionsTests'
    }

    if ([string]::IsNullOrWhiteSpace($env:RJ_POSTGRES_CONNECTION)) {
        Write-Error 'BLOCKED: non-PostgreSQL Wave E tests completed, but RJ_POSTGRES_CONNECTION is required for persistence/runtime closure.'
        exit 2
    }

    Invoke-GateStep 'Wave D regression gate' { & $waveDScript }
    Invoke-GateStep 'PostgreSQL maintenance ordering test' {
        dotnet test $integrationProject --no-restore --filter 'FullyQualifiedName~PostgresProcessSummaryMaintenanceTests'
    }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }
}
finally {
    Pop-Location
}
