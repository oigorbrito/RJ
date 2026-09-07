param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$projectPath = Join-Path $repoRoot 'tests\RJ.DomainTests\RJ.DomainTests.csproj'
$filter = 'FullyQualifiedName~LocalRagEvaluationTests|FullyQualifiedName~ResponseFixtureIngestionTests|FullyQualifiedName~Wave1CorpusContractTests|FullyQualifiedName~CorpusAdmissionServiceTests'

Push-Location $repoRoot
try {
    & dotnet test $projectPath --filter $filter
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
