param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$integrationProject = Join-Path $repoRoot 'tests\RJ.IntegrationTests\RJ.IntegrationTests.csproj'
$httpProject = Join-Path $repoRoot 'tests\RJ.HttpContractTests\RJ.HttpContractTests.csproj'
$apiProject = Join-Path $repoRoot 'src\RJ.Api\RJ.Api.csproj'
$m1Script = Join-Path $scriptRoot 'test-rjudi-m1.ps1'

if ([string]::IsNullOrWhiteSpace($env:RJ_POSTGRES_CONNECTION)) {
    Write-Error 'BLOCKED: RJ_POSTGRES_CONNECTION is required for Wave D PostgreSQL execution.'
    exit 2
}

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
    Invoke-GateStep 'deterministic M1 + security/persistence coordinator gate' { & $m1Script }
    Invoke-GateStep 'PostgreSQL schema and process-summary persistence tests' {
        dotnet test $integrationProject --no-restore --filter 'FullyQualifiedName~PostgresSchemaTests|FullyQualifiedName~PostgresProcessSummaryPersistenceTests'
    }
    Invoke-GateStep 'HTTP contracts with PostgreSQL runtime' { dotnet test $httpProject --no-restore }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }
}
finally {
    Pop-Location
}
