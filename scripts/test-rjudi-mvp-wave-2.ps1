param(
    [string]$ApiUrl = "http://127.0.0.1:5001",
    [string]$DatasetPath = "demo-data/processes.json"
)

$ErrorActionPreference = "Stop"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

$connectionString = $env:RJ_POSTGRES_CONNECTION
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Fail "RJ_POSTGRES_CONNECTION is required."
}

$datasetFullPath = Join-Path $repoRoot $DatasetPath
if (-not (Test-Path $datasetFullPath)) {
    Fail "Dataset not found: $datasetFullPath"
}

$artifactRoot = Join-Path $repoRoot ".artifacts\rjudi-mvp-wave-2"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactPath = Join-Path $artifactRoot "$timestamp.json"

$head = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()
$worktree = @(git status --porcelain)
$dotnetInfo = (& dotnet --info | Out-String)
$datasetHash = (Get-FileHash -Algorithm SHA256 $datasetFullPath).Hash.ToLowerInvariant()

$cases = Get-Content $datasetFullPath -Raw | ConvertFrom-Json
if (-not $cases -or $cases.Count -eq 0) {
    Fail "Dataset contains no cases."
}

$startedAt = Get-Date
$steps = New-Object System.Collections.Generic.List[object]
$apiProcess = $null

try {
    & dotnet run --project .\tools\RJ.DatabaseMigrator\RJ.DatabaseMigrator.csproj
    if ($LASTEXITCODE -ne 0) { Fail "Database migration failed." }
    $steps.Add([pscustomobject]@{ name = "migration"; status = "PASS"; exitCode = 0 })

    & dotnet run --project .\tools\RJ.DemoSeeder\RJ.DemoSeeder.csproj -- $datasetFullPath
    if ($LASTEXITCODE -ne 0) { Fail "Demo seed failed." }
    $steps.Add([pscustomobject]@{ name = "seed"; status = "PASS"; exitCode = 0 })

    $existing = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
    if ($existing) {
        Fail "Port 5001 is already in use. Stop the existing listener before running the gate."
    }

    $env:RJUDI_DEMO_MODE = "true"
    $env:ASPNETCORE_URLS = $ApiUrl
    $apiOut = Join-Path $artifactRoot "$timestamp-api.stdout.log"
    $apiErr = Join-Path $artifactRoot "$timestamp-api.stderr.log"

    $apiProcess = Start-Process dotnet \
        -ArgumentList @("run", "--project", ".\src\RJ.Api\RJ.Api.csproj") \
        -WorkingDirectory $repoRoot \
        -RedirectStandardOutput $apiOut \
        -RedirectStandardError $apiErr \
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
    if (-not $healthy) { Fail "API did not become healthy within the gate startup window." }
    $steps.Add([pscustomobject]@{ name = "api-health"; status = "PASS"; exitCode = 0 })

    $lookupResults = @()
    foreach ($case in $cases) {
        $response = Invoke-RestMethod "$ApiUrl/api/processes/by-cnj/$($case.cnj)"
        if ($response.caseId -ne $case.caseId) {
            Fail "CNJ lookup mismatch for $($case.cnj): expected $($case.caseId), got $($response.caseId)."
        }
        if ($response.cnj -ne $case.cnj) {
            Fail "CNJ normalization mismatch for $($case.caseId)."
        }
        $lookupResults += [pscustomobject]@{
            cnj = $case.cnj
            caseId = $response.caseId
            name = $response.process.name
            status = $response.process.status
        }
    }
    $steps.Add([pscustomobject]@{ name = "multi-cnj-lookup"; status = "PASS"; exitCode = 0; observations = $lookupResults })

    $documentResults = @()
    foreach ($case in $cases) {
        $documents = Invoke-RestMethod "$ApiUrl/api/cases/$($case.caseId)/documents"
        if (-not $documents.items -or $documents.items.Count -lt 1) {
            Fail "No documents returned for $($case.caseId)."
        }
        $documentResults += [pscustomobject]@{
            caseId = $case.caseId
            documentCount = $documents.items.Count
        }
    }
    $steps.Add([pscustomobject]@{ name = "documents-by-case"; status = "PASS"; exitCode = 0; observations = $documentResults })

    $negativeStatus = $null
    try {
        Invoke-WebRequest "$ApiUrl/api/processes/by-cnj/6003999-61.2026.8.16.0021" -UseBasicParsing | Out-Null
        Fail "Unknown CNJ unexpectedly returned success."
    }
    catch {
        if ($_.Exception.Response) {
            $negativeStatus = [int]$_.Exception.Response.StatusCode
        }
        if ($negativeStatus -ne 404) {
            throw
        }
    }
    $steps.Add([pscustomobject]@{ name = "unknown-cnj"; status = "PASS"; httpStatus = 404 })

    $result = [pscustomobject]@{
        gate = "RJUDI_MVP_WAVE_2_V1"
        status = "PASS"
        methodologicalClassification = "DERIVED_FROM_METHOD"
        projectDecisionNotes = @(
            "Five demo cases are a project-scoped fixture count, not an empirical sample-size claim.",
            "The 60-second API startup window is an operational project decision, not an empirically derived threshold."
        )
        git = [pscustomobject]@{
            branch = $branch
            commit = $head
            worktreeDirty = ($worktree.Count -gt 0)
            worktreeStatus = $worktree
        }
        environment = [pscustomobject]@{
            dotnetInfo = $dotnetInfo
            apiUrl = $ApiUrl
        }
        dataset = [pscustomobject]@{
            path = $DatasetPath
            sha256 = $datasetHash
            caseCount = $cases.Count
        }
        startedAt = $startedAt.ToString("o")
        finishedAt = (Get-Date).ToString("o")
        steps = $steps
    }

    $result | ConvertTo-Json -Depth 20 | Set-Content $artifactPath -Encoding utf8
    Write-Host "RJUDI_MVP_WAVE_2_V1 PASS"
    Write-Host "artifact=$artifactPath"
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
