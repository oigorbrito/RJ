param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$solution = Join-Path $repoRoot 'RJ.slnx'
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$toolProject = Join-Path $repoRoot 'tools\RJ.EmpiricalSelectionVerifier\RJ.EmpiricalSelectionVerifier.csproj'

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
    Invoke-GateStep 'empirical selection model, manifest and Pareto decision tests' {
        dotnet test $domainProject --no-restore --filter 'FullyQualifiedName~EmpiricalSelectionServiceTests|FullyQualifiedName~EmpiricalSelectionManifestTests'
    }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }

    $manifest = $env:RJ_EMPIRICAL_SELECTION_MANIFEST_PATH
    $manifestSha = $env:RJ_EMPIRICAL_SELECTION_MANIFEST_SHA256
    $artifactRoot = $env:RJ_EMPIRICAL_SELECTION_ARTIFACT_ROOT

    if ([string]::IsNullOrWhiteSpace($manifest) `
        -or [string]::IsNullOrWhiteSpace($manifestSha) `
        -or [string]::IsNullOrWhiteSpace($artifactRoot)) {
        Write-Error 'BLOCKED RJ-BLK-003: admitted EVAL-010 paired selection evidence is required through RJ_EMPIRICAL_SELECTION_MANIFEST_PATH, RJ_EMPIRICAL_SELECTION_MANIFEST_SHA256 and RJ_EMPIRICAL_SELECTION_ARTIFACT_ROOT.'
        exit 2
    }

    Invoke-GateStep 'verify immutable paired empirical selection evidence and produce decision' {
        dotnet run --project $toolProject --no-build -- $manifest $manifestSha $artifactRoot
    }
}
finally {
    Pop-Location
}
