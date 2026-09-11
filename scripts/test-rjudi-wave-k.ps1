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
    Invoke-Step 'build solution' { dotnet build RJ.slnx --no-restore }
    Invoke-Step 'Wave K focused domain tests' {
        dotnet test tests/RJ.DomainTests/RJ.DomainTests.csproj --no-restore --filter 'FullyQualifiedName~RetrievalBenchmarkTests|FullyQualifiedName~EmpiricalRawObservationArtifactTests|FullyQualifiedName~GenerationEmpiricalObservationMaterializerTests'
    }
    Invoke-Step 'git diff check' { git diff --check }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("rjudi-wave-k-" + [Guid]::NewGuid().ToString('N'))
    $artifactRoot = Join-Path $tempRoot 'artifacts'
    $reportDir = Join-Path $artifactRoot 'selftest'
    $policyDir = Join-Path $artifactRoot 'policies'
    $configDir = Join-Path $artifactRoot 'configs'
    $outputDir = Join-Path $artifactRoot 'raw'
    $reportPath = Join-Path $reportDir 'retrieval-report.json'
    $policyPath = Join-Path $policyDir 'r0-selftest.json'
    $configPath = Join-Path $configDir 'r0-selftest.json'
    New-Item -ItemType Directory -Path $reportDir,$policyDir,$configDir -Force | Out-Null

    try {
        '{"implementation":"fake-retrieval-selftest-v1","limit":5}' | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
        $configSha = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash.ToLowerInvariant()

        @{
            formatVersion = 'rjudi-retrieval-benchmark-report-v1'
            catalogVersion = 'wave-k-selftest-catalog-v1'
            treatment = @{
                treatmentId = 'R0-selftest'
                implementationId = 'fake-retrieval-selftest-v1'
                configurationReference = 'configs/r0-selftest.json'
                configurationSha256 = $configSha
            }
            cases = @(
                @{
                    caseId = 'case-selftest-1'
                    queryCount = 2
                    hitAt1 = 1
                    hitAt3 = 2
                    hitAt5 = 2
                    mrr = 0.75
                    durationMs = 1.25
                    queries = @(
                        @{ queryId = 'q1'; firstRelevantRank = 1; hitAt1 = $true; hitAt3 = $true; hitAt5 = $true },
                        @{ queryId = 'q2'; firstRelevantRank = 2; hitAt1 = $false; hitAt3 = $true; hitAt5 = $true }
                    )
                    errorType = $null
                    errorMessage = $null
                }
            )
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
        $reportSha = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()

        @{
            treatmentId = 'R0-selftest'
            implementationId = 'fake-retrieval-selftest-v1'
            configurationReference = 'configs/r0-selftest.json'
            configurationSha256 = $configSha
            hitAt1RateMetricId = 'hit_at_1_rate'
            hitAt3RateMetricId = 'hit_at_3_rate'
            hitAt5RateMetricId = 'hit_at_5_rate'
            mrrMetricId = 'mrr'
            retrievalDurationMsMetricId = 'retrieval_duration_ms'
            candidateExecutionFailureGateId = 'candidate_execution_failure'
        } | ConvertTo-Json | Set-Content -LiteralPath $policyPath -Encoding utf8NoBOM
        $policySha = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $recordedAt = [DateTimeOffset]::UtcNow.ToString('o')

        Invoke-Step 'materialize retrieval empirical raw observation' {
            dotnet run --project tools/RJ.RetrievalObservationMaterializer/RJ.RetrievalObservationMaterializer.csproj --no-build -- `
                $reportPath $reportSha 'selftest/retrieval-report.json' $recordedAt `
                $policyPath $policySha 'policies/r0-selftest.json' $outputDir
        }

        $observations = @(Get-ChildItem -LiteralPath $outputDir -Filter '*.empirical-observation.json' -File)
        if ($observations.Count -ne 1) { throw "Expected exactly 1 retrieval observation, observed $($observations.Count)." }
        $indexPath = Join-Path $outputDir 'R0-selftest.empirical-observation-index.json'
        if (-not (Test-Path -LiteralPath $indexPath)) { throw 'Retrieval observation materialization index is missing.' }

        $observation = Get-Content -Raw -LiteralPath $observations[0].FullName | ConvertFrom-Json
        if ($observation.formatVersion -ne 'rjudi-empirical-raw-observation-v3') { throw 'Unexpected raw observation format.' }
        if ($observation.sourceArtifactReference -ne 'selftest/retrieval-report.json' -or $observation.sourceArtifactSha256 -ne $reportSha) {
            throw 'Retrieval source-report provenance drift.'
        }
        if ($observation.materializationPolicyReference -ne 'policies/r0-selftest.json' -or $observation.materializationPolicySha256 -ne $policySha) {
            throw 'Retrieval materialization-policy provenance drift.'
        }
        if ($observation.measurements.hit_at_1_rate -ne 0.5 -or $observation.measurements.hit_at_3_rate -ne 1.0 -or $observation.measurements.mrr -ne 0.75) {
            throw 'Retrieval measurement materialization drift.'
        }

        Write-Host "Wave K self-test report SHA-256: $reportSha"
        Write-Host "Wave K policy SHA-256: $policySha"
        Write-Host "Wave K configuration SHA-256: $configSha"
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    }

    Write-Host 'WAVE_K_GATE=PASS'
    exit 0
}
catch {
    Write-Error $_
    Write-Host 'WAVE_K_GATE=FAIL'
    exit 1
}
finally { Pop-Location }
