param(
    [string]$ApiUrl = "http://127.0.0.1:5002",
    [string]$FixturePath = "demo-data/process-summary-openai-case.json"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($env:RJ_POSTGRES_CONNECTION)) {
    Fail "RJ_POSTGRES_CONNECTION is required."
}
if ([string]::IsNullOrWhiteSpace($env:RJ_GENERATION_MODEL)) {
    Fail "BLOCKED: RJ_GENERATION_MODEL is required for the live OpenAI gate."
}
if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    Fail "BLOCKED: OPENAI_API_KEY is required locally for the live OpenAI gate. Do not paste or commit the key."
}

$fixtureFullPath = Join-Path $repoRoot $FixturePath
if (-not (Test-Path $fixtureFullPath)) {
    Fail "Generation fixture not found: $fixtureFullPath"
}

$artifactRoot = Join-Path $repoRoot ".artifacts\rjudi-mvp-wave-3"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactPath = Join-Path $artifactRoot "$timestamp.json"
$apiOut = Join-Path $artifactRoot "$timestamp-api.stdout.log"
$apiErr = Join-Path $artifactRoot "$timestamp-api.stderr.log"

$head = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()
$worktree = @(git status --porcelain)
$fixtureHash = (Get-FileHash -Algorithm SHA256 $fixtureFullPath).Hash.ToLowerInvariant()
$dotnetInfo = (& dotnet --info | Out-String)
$startedAt = Get-Date
$steps = New-Object System.Collections.Generic.List[object]
$apiProcess = $null
$previousProvider = $env:RJ_GENERATION_PROVIDER
$previousUrls = $env:ASPNETCORE_URLS
$previousDemoMode = $env:RJUDI_DEMO_MODE

try {
    Write-Host "[wave3] build"
    & dotnet build RJ.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { Fail "Build failed." }
    $steps.Add([pscustomobject]@{ name = "build"; status = "PASS"; exitCode = 0 })

    Write-Host "[wave3] api tests"
    & dotnet test .\tests\RJ.ApiTests\RJ.ApiTests.csproj --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { Fail "RJ.ApiTests failed." }
    $steps.Add([pscustomobject]@{ name = "api-tests"; status = "PASS"; exitCode = 0 })

    Write-Host "[wave3] migration"
    & dotnet run --project .\tools\RJ.DatabaseMigrator\RJ.DatabaseMigrator.csproj
    if ($LASTEXITCODE -ne 0) { Fail "Database migration failed." }
    $steps.Add([pscustomobject]@{ name = "migration"; status = "PASS"; exitCode = 0 })

    Write-Host "[wave3] seed"
    & dotnet run --project .\tools\RJ.DemoSeeder\RJ.DemoSeeder.csproj -- (Join-Path $repoRoot "demo-data\processes.json")
    if ($LASTEXITCODE -ne 0) { Fail "Demo seed failed." }
    $steps.Add([pscustomobject]@{ name = "seed"; status = "PASS"; exitCode = 0 })

    $uri = [Uri]$ApiUrl
    $port = $uri.Port
    $existing = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($existing) {
        Fail "Port $port is already in use. Stop the existing listener before running the gate."
    }

    $env:RJ_GENERATION_PROVIDER = "openai"
    $env:RJUDI_DEMO_MODE = "true"
    $env:ASPNETCORE_URLS = $ApiUrl

    Write-Host "[wave3] starting API provider=openai model=$env:RJ_GENERATION_MODEL url=$ApiUrl"
    $apiProcess = Start-Process dotnet `
        -ArgumentList @("run", "--project", ".\src\RJ.Api\RJ.Api.csproj", "--configuration", "Release", "--no-build") `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $apiOut `
        -RedirectStandardError $apiErr `
        -PassThru

    $deadline = (Get-Date).AddSeconds(60)
    $healthy = $false
    while ((Get-Date) -lt $deadline) {
        if ($apiProcess.HasExited) {
            Fail "API exited before becoming healthy. See $apiErr"
        }
        try {
            $health = Invoke-RestMethod "$ApiUrl/health/live" -TimeoutSec 2
            if ($null -ne $health) {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $healthy) { Fail "API did not become healthy within the startup window. See $apiErr" }
    Write-Host "[wave3] API healthy"
    $steps.Add([pscustomobject]@{ name = "api-health-openai"; status = "PASS"; exitCode = 0 })

    Write-Host "[wave3] loading controlled fixture"
    $rawProcess = [System.IO.File]::ReadAllText($fixtureFullPath)
    Write-Host "[wave3] fixture loaded chars=$($rawProcess.Length)"

    Write-Host "[wave3] preparing request envelope"
    $request = @{
        idempotencyKey = "wave3-$timestamp"
        sourceSystem = "judit"
        sourceName = "RJudi MVP Wave 3"
        sourceReference = "demo-case-001.json"
        rawContent = $rawProcess
        observedAt = "2026-09-11T17:50:00-03:00"
        instruction = "Produza um resumo objetivo do processo, destacando situação atual e principais acontecimentos."
    }

    Write-Host "[wave3] serializing request body"
    $body = $request | ConvertTo-Json -Depth 20 -Compress
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    Write-Host "[wave3] request body ready chars=$($body.Length) utf8Bytes=$($bodyBytes.Length)"

    Write-Host "[wave3] submitting live OpenAI process summary (bounded request timeout: 90s)"
    try {
        $submission = Invoke-RestMethod `
            -Method Post `
            -Uri "$ApiUrl/api/process-summaries/jobs" `
            -ContentType "application/json; charset=utf-8" `
            -Body $bodyBytes `
            -TimeoutSec 90
    }
    catch {
        $httpBody = $null
        if ($_.Exception.Response) {
            try {
                $responseStream = $_.Exception.Response.GetResponseStream()
                if ($responseStream) {
                    $reader = New-Object System.IO.StreamReader($responseStream)
                    $httpBody = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            }
            catch {
                $httpBody = $null
            }
        }

        Write-Host "[wave3] submission failed or timed out."
        if (-not [string]::IsNullOrWhiteSpace($httpBody)) {
            Write-Host "[wave3] HTTP response body: $httpBody"
        }
        Write-Host "[wave3] API stderr: $apiErr"
        if (Test-Path $apiErr) { Get-Content $apiErr -Tail 80 }
        Write-Host "[wave3] API stdout: $apiOut"
        if (Test-Path $apiOut) { Get-Content $apiOut -Tail 80 }
        throw
    }

    Write-Host "[wave3] submission returned status=$($submission.status) isValid=$($submission.isValid)"
    if ([string]::IsNullOrWhiteSpace($submission.jobId)) {
        Fail "Process-summary submission did not return jobId."
    }
    if ($submission.caseId -ne "demo-case-001") {
        Fail "Unexpected caseId from process-summary submission: $($submission.caseId)"
    }
    if ($submission.status -ne "Validated" -or -not $submission.isValid) {
        Fail "Live OpenAI process summary was not validated. status=$($submission.status) isValid=$($submission.isValid)"
    }

    Write-Host "[wave3] reading validated summary"
    $validated = Invoke-RestMethod `
        -Uri "$ApiUrl/api/process-summaries/jobs/$($submission.jobId)/validated-summary" `
        -TimeoutSec 30

    $claims = @($validated.claims)
    if ($claims.Count -lt 1) {
        Fail "Validated live OpenAI summary contains no claims."
    }

    $citationCount = 0
    foreach ($claim in $claims) {
        $citationCount += @($claim.citations).Count
    }
    if ($citationCount -lt 1) {
        Fail "Validated live OpenAI summary contains no citations."
    }

    $steps.Add([pscustomobject]@{
        name = "live-openai-process-summary"
        status = "PASS"
        jobId = $submission.jobId
        caseId = $submission.caseId
        validationStatus = $submission.status
        claimCount = $claims.Count
        citationCount = $citationCount
    })

    $result = [pscustomobject]@{
        gate = "RJUDI_MVP_WAVE_3_V1"
        status = "PASS"
        methodologicalClassification = "DERIVED_FROM_METHOD"
        scope = "single controlled Judit fixture through API runtime with OpenAI provider and downstream validation"
        git = [pscustomobject]@{
            branch = $branch
            commit = $head
            worktreeDirty = ($worktree.Count -gt 0)
            worktreeStatus = $worktree
        }
        environment = [pscustomobject]@{
            dotnetInfo = $dotnetInfo
            apiUrl = $ApiUrl
            provider = "openai"
            model = $env:RJ_GENERATION_MODEL
            apiKeyPresent = $true
        }
        fixture = [pscustomobject]@{
            path = $FixturePath
            sha256 = $fixtureHash
        }
        startedAt = $startedAt.ToString("o")
        finishedAt = (Get-Date).ToString("o")
        steps = $steps
    }

    $result | ConvertTo-Json -Depth 20 | Set-Content $artifactPath -Encoding utf8
    Write-Host "RJUDI_MVP_WAVE_3_V1 PASS"
    Write-Host "artifact=$artifactPath"
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $env:RJ_GENERATION_PROVIDER = $previousProvider
    $env:ASPNETCORE_URLS = $previousUrls
    $env:RJUDI_DEMO_MODE = $previousDemoMode
}
