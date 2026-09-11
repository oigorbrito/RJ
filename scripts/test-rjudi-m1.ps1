param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$domainProjectPath = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$apiProjectPath = Join-Path $repoRoot 'tests\RJ.ApiTests\RJ.ApiTests.csproj'
$apiBuildProjectPath = Join-Path $repoRoot 'src\RJ.Api\RJ.Api.csproj'

$domainFilter = 'FullyQualifiedName~RJ.DomainTests.ProcessSummaryCorrectiveRetryTests|FullyQualifiedName~RJ.DomainTests.ProcessSummaryRefreshPlannerTests|FullyQualifiedName~RJ.DomainTests.ProcessSummaryObservabilityCatalogTests|FullyQualifiedName~RJ.DomainTests.ProcessAttachmentContentAdmissionTests|FullyQualifiedName~RJ.DomainTests.RjudiProcessBenchmarkCatalogFactoryTests|FullyQualifiedName~RJ.DomainTests.LegalCaseConsistencyEngineTests|FullyQualifiedName~RJ.DomainTests.ProcessGenerationContextComposerTests|FullyQualifiedName~RJ.DomainTests.GenerationContextServiceAuthorizationTests|FullyQualifiedName~RJ.DomainTests.ProcessSummaryJobServiceTests|FullyQualifiedName~RJ.DomainTests.ProcessSecurityPolicyTests|FullyQualifiedName~RJ.DomainTests.ProcessSummaryValidatorTests|FullyQualifiedName~RJ.DomainTests.ProcessSummaryGenerationTests|FullyQualifiedName~RJ.DomainTests.LegalCaseMergeServiceTests|FullyQualifiedName~RJ.DomainTests.ProcessNormalizationTests|FullyQualifiedName~RJ.DomainTests.JuditProcessSourceAdapterTests|FullyQualifiedName~RJ.DomainTests.ProcessSourceCanonicalizationServiceTests|FullyQualifiedName~RJ.DomainTests.Wave1CorpusContractTests|FullyQualifiedName~RJ.DomainTests.LegalCaseTests|FullyQualifiedName~RJ.DomainTests.LegalDocumentTests'
$apiFilter = 'FullyQualifiedName~RJ.ApiTests.ProcessSummaryEndpointTests|FullyQualifiedName~RJ.ApiTests.ProcessSummarySecurityBoundaryTests'

function Invoke-GateStep {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,
        [Parameter(Mandatory = $true)]
        [scriptblock] $Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Push-Location $repoRoot
try {
    Invoke-GateStep 'build RJ.Api' { dotnet build $apiBuildProjectPath --no-restore }
    Invoke-GateStep 'domain RJudi M1 tests' { dotnet test $domainProjectPath --no-restore --filter $domainFilter }
    Invoke-GateStep 'api RJudi endpoint/security tests' { dotnet test $apiProjectPath --no-restore --filter $apiFilter }
    Invoke-GateStep 'git diff whitespace check' { git diff --check }
}
finally {
    Pop-Location
}
