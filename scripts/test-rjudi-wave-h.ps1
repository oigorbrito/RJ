param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$solution = Join-Path $repoRoot 'RJ.slnx'
$domainProject = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$toolProject = Join-Path $repoRoot 'tools\RJ.AttachmentAdmissionVerifier\RJ.AttachmentAdmissionVerifier.csproj'

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
    Invoke-GateStep 'attachment admission and existing chunk/content tests' {
        dotnet test $domainProject --no-restore --filter 'FullyQualifiedName~AttachmentAdmissionServiceTests|FullyQualifiedName~ProcessAttachmentContentAdmissionTests|FullyQualifiedName~ProcessAttachmentChunkerTests'
    }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }

    $manifest = $env:RJ_ATT_MANIFEST_PATH
    $manifestSha = $env:RJ_ATT_MANIFEST_SHA256
    $artifactRoot = $env:RJ_ATT_ARTIFACT_ROOT
    $canonicalSource = $env:RJ_ATT_CANONICAL_SOURCE_PATH

    if ([string]::IsNullOrWhiteSpace($manifest) `
        -or [string]::IsNullOrWhiteSpace($manifestSha) `
        -or [string]::IsNullOrWhiteSpace($artifactRoot) `
        -or [string]::IsNullOrWhiteSpace($canonicalSource)) {
        Write-Error 'BLOCKED ATT-001: RJ_ATT_MANIFEST_PATH, RJ_ATT_MANIFEST_SHA256, RJ_ATT_ARTIFACT_ROOT and RJ_ATT_CANONICAL_SOURCE_PATH are required.'
        exit 2
    }

    Invoke-GateStep 'verify authorized attachment binary and independent extraction evidence' {
        dotnet run --project $toolProject --no-build -- $manifest $manifestSha $artifactRoot $canonicalSource
    }
}
finally {
    Pop-Location
}
