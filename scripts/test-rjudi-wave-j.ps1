$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Invoke-Step 'build solution' {
        dotnet build RJ.slnx --no-restore
    }

    Invoke-Step 'Wave J focused domain tests' {
        dotnet test tests/RJ.DomainTests/RJ.DomainTests.csproj --no-restore --filter 'FullyQualifiedName~GenerationEmpiricalObservationMaterializerTests|FullyQualifiedName~EmpiricalRawObservationArtifactTests|FullyQualifiedName~GenerationBenchmarkRunnerTests'
    }

    Invoke-Step 'git diff check' {
        git diff --check
    }

    $head = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
        throw 'Unable to resolve exact git HEAD.'
    }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("rjudi-wave-j-" + [Guid]::NewGuid().ToString('N'))
    $outputDir = Join-Path $tempRoot 'observations'
    $reportPath = Join-Path $tempRoot 'generation-report.json'
    $policyPath = Join-Path $tempRoot 'materialization-policy.json'
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        $runtime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        Invoke-Step 'deterministic generation benchmark self-test' {
            dotnet run --project src/RJ.BenchmarkCli/RJ.BenchmarkCli.csproj --no-build -- `
                --git-commit $head `
                --runtime $runtime `
                --model-id 'harness-selftest-v1' `
                --model-config 'deterministic-selftest' `
                --seed 'wave-j-fixed' `
                --output $reportPath
        }

        if (-not (Test-Path -LiteralPath $reportPath)) {
            throw 'Generation benchmark did not produce its report artifact.'
        }

        $reportSha = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        @{
            claimRecallMetricId = 'claim_recall'
            citationValidityMetricId = 'citation_validity'
            groundednessMetricId = 'groundedness'
            candidateExecutionFailureGateId = 'candidate_execution_failure'
        } | ConvertTo-Json | Set-Content -LiteralPath $policyPath -Encoding utf8NoBOM
        $policySha = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $recordedAt = [DateTimeOffset]::UtcNow.ToString('o')

        Invoke-Step 'materialize empirical raw observations' {
            dotnet run --project tools/RJ.GenerationObservationMaterializer/RJ.GenerationObservationMaterializer.csproj --no-build -- `
                $reportPath `
                $reportSha `
                'G0' `
                'selftest/generation-report.json' `
                $recordedAt `
                $policyPath `
                $policySha `
                $outputDir
        }

        $observations = @(Get-ChildItem -LiteralPath $outputDir -Filter '*.empirical-observation.json' -File)
        if ($observations.Count -ne 2) {
            throw "Expected exactly 2 materialized observations, observed $($observations.Count)."
        }

        $indexPath = Join-Path $outputDir 'G0.empirical-observation-index.json'
        if (-not (Test-Path -LiteralPath $indexPath)) {
            throw 'Observation materialization index is missing.'
        }

        foreach ($observationFile in $observations) {
            $observation = Get-Content -Raw -LiteralPath $observationFile.FullName | ConvertFrom-Json
            if ($observation.formatVersion -ne 'rjudi-empirical-raw-observation-v2') {
                throw "Unexpected raw observation format in $($observationFile.Name)."
            }
            if ($observation.sourceArtifactReference -ne 'selftest/generation-report.json') {
                throw "Source artifact reference drift in $($observationFile.Name)."
            }
            if ($observation.sourceArtifactSha256 -ne $reportSha) {
                throw "Source artifact SHA-256 drift in $($observationFile.Name)."
            }
        }

        Write-Host "Wave J self-test report SHA-256: $reportSha"
        Write-Host "Wave J policy SHA-256: $policySha"
        Write-Host "Wave J observation count: $($observations.Count)"
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }

    Write-Host 'WAVE_J_GATE=PASS'
    exit 0
}
catch {
    Write-Error $_
    Write-Host 'WAVE_J_GATE=FAIL'
    exit 1
}
finally {
    Pop-Location
}
