param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$solution = Join-Path $repoRoot 'RJ.slnx'
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$verifierProject = Join-Path $repoRoot 'tools\RJ.DataJudFixtureVerifier\RJ.DataJudFixtureVerifier.csproj'

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
    Invoke-GateStep 'build solution' { dotnet build $solution --no-restore }
    Invoke-GateStep 'DataJud documented schema contract tests' {
        dotnet test $domainProject --no-restore --filter 'FullyQualifiedName~DataJudPublicApiContractTests'
    }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }

    $required = @(
        'RJ_DATAJUD_FIXTURE_PATH',
        'RJ_DATAJUD_EXPECTED_CNJ',
        'RJ_DATAJUD_SOURCE_REFERENCE',
        'RJ_DATAJUD_OBSERVED_AT'
    )
    $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -gt 0) {
        Write-Error ("BLOCKED SRC-003: authentic DataJud fixture verification requires: " + ($missing -join ', '))
        exit 2
    }

    Invoke-GateStep 'authentic DataJud fixture verification' {
        dotnet run --project $verifierProject --no-build -- `
            $env:RJ_DATAJUD_FIXTURE_PATH `
            $env:RJ_DATAJUD_EXPECTED_CNJ `
            $env:RJ_DATAJUD_SOURCE_REFERENCE `
            $env:RJ_DATAJUD_OBSERVED_AT
    }
}
finally {
    Pop-Location
}
