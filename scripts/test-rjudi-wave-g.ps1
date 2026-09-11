param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$solution = Join-Path $repoRoot 'RJ.slnx'
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$toolProject = Join-Path $repoRoot 'tools\RJ.Eval010CorpusVerifier\RJ.Eval010CorpusVerifier.csproj'

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
    Invoke-GateStep 'EVAL-010 corpus admission and oracle-isolation contract tests' {
        dotnet test $domainProject --no-restore --filter 'FullyQualifiedName~Eval010CorpusAdmissionTests|FullyQualifiedName~CorpusAdmission'
    }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }

    $manifest = $env:RJ_EVAL010_MANIFEST_PATH
    $artifactRoot = $env:RJ_EVAL010_ARTIFACT_ROOT
    $catalog = $env:RJ_EVAL010_BENCHMARK_CATALOG_PATH

    if ([string]::IsNullOrWhiteSpace($manifest) -or [string]::IsNullOrWhiteSpace($artifactRoot)) {
        Write-Error 'BLOCKED RJ-BLK-003: RJ_EVAL010_MANIFEST_PATH and RJ_EVAL010_ARTIFACT_ROOT are required for real-corpus admission.'
        exit 2
    }

    $arguments = @(
        'run', '--project', $toolProject, '--no-build', '--',
        $manifest,
        $artifactRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($catalog)) {
        $arguments += $catalog
    }

    Invoke-GateStep 'verify frozen EVAL-010 corpus and oracle isolation' { dotnet @arguments }
}
finally {
    Pop-Location
}
