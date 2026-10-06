# DataJud public API source boundary

Status: SRC-003 partial closure. The public contract is now documented and executable, but authentic-response validation remains BLOCKED until an observed DataJud response is captured and verified on the exact code revision.

## Authoritative external basis observed on 2026-09-11

Official CNJ DataJud documentation establishes that:

- the public API base is `https://api-publica.datajud.cnj.jus.br/`;
- searches are performed against tribunal-specific aliases such as `api_publica_tjpr/_search`;
- the documented process-number field is `numeroProcesso`, represented as the unformatted 20-digit CNJ number;
- the public glossary documents `tribunal`, `numeroProcesso`, `grau`, `nivelSigilo`, `classe`, `assuntos`, `orgaoJulgador` and `movimentos` plus the documented nested fields used by `DataJudPublicApiContract`;
- current public authentication is `Authorization: APIKey [public key published by CNJ]`;
- the public API provides metadata for public judicial processes and protects confidential/sealed information according to CNJ rules.

Authoritative references:

- https://datajud-wiki.cnj.jus.br/api-publica/
- https://datajud-wiki.cnj.jus.br/api-publica/acesso/
- https://datajud-wiki.cnj.jus.br/api-publica/endpoints/
- https://datajud-wiki.cnj.jus.br/api-publica/glossario/
- https://www.cnj.jus.br/sistemas/datajud/api-publica/

## Implemented contract

`DataJudPublicApiContract` provides three deliberately narrow capabilities:

1. resolve a tribunal alias to the documented public `_search` endpoint;
2. create a deterministic process-number query using only `numeroProcesso`;
3. parse exactly one Elasticsearch-style hit into `DataJudProcessObservation`.

The parser is fail-closed:

- source system must be `datajud-public-api`;
- expected CNJ must contain exactly 20 digits after formatting is removed;
- response must contain exactly one hit;
- returned `numeroProcesso` must match the requested CNJ;
- required documented fields must have the expected structural type;
- missing or structurally incompatible required fields are rejected;
- raw response content is hashed with SHA-256 and the source reference and observation time are retained.

The typed observation includes only fields documented by the current public glossary. It does not manufacture a complete `LegalCase`.

## Why this is not yet an `IProcessSourceAdapter`

`IProcessSourceAdapter.Canonicalize` returns a complete `LegalCase`. The public DataJud contract currently observed does not demonstrate all fields required by that aggregate, including the project-specific complete name/phase/status/amount/lawyer/attachment representation.

Creating placeholder values for those fields would turn source absence into invented facts. Therefore Wave F intentionally stops at a typed enrichment observation until authentic source evidence and an explicit field-level merge policy are both available.

Classification:

- documented endpoint/query/schema: authoritative external source contract;
- parser, fail-closed rules and typed observation: DERIVED_FROM_METHOD / PROJECT_DECISION;
- raw SHA/reference/observed-at preservation: REPRODUCIBILITY_SUPPORTED;
- full DataJud-to-`LegalCase` adapter: NOT_DEMONSTRATED;
- DataJud enrichment merge policy: REQUIRES_EMPIRICAL_EVALUATION / PROJECT_DECISION until authentic observations are admitted.

## Contract tests vs authentic evidence

`DataJudPublicApiContractTests` use minimal contract-shaped JSON authored for testing. They prove parser behavior only.

They MUST NOT be described as:

- authentic DataJud fixtures;
- evidence that the live API currently returns the tested payload;
- corpus evidence;
- end-to-end DataJud integration.

## Authentic fixture verifier and capture tool

`tools/RJ.DataJudFixtureVerifier` uses the exact production parser for both verification and capture.

Verification usage:

```powershell
dotnet run --project tools/RJ.DataJudFixtureVerifier/RJ.DataJudFixtureVerifier.csproj -- `
  <fixture.json> `
  <expected-cnj> `
  <source-reference> `
  <observed-at-iso8601>
```

Capture usage:

```powershell
$env:RJ_DATAJUD_API_KEY = '<current CNJ-published API key>'
dotnet run --project tools/RJ.DataJudFixtureVerifier/RJ.DataJudFixtureVerifier.csproj -- `
  capture tjpr 60031603620268160021 C:\evidence\datajud-tjpr-case-001.json
```

Capture mode:

- obtains the endpoint through `DataJudPublicApiContract.SearchEndpoint`;
- creates the same deterministic `numeroProcesso` query used by the contract;
- reads the API key only from `RJ_DATAJUD_API_KEY` and never writes/logs it;
- requires HTTP success;
- validates CNJ and documented schema before persisting the body;
- writes the exact raw body to the requested path;
- writes `<output>.metadata.json` containing source reference, normalized CNJ, observed-at timestamp, SHA-256 and fixture path.

The API key itself is deliberately not committed because the CNJ documentation states that the published key may change.

Exit codes:

- `0`: verification/capture succeeded and the payload satisfies the implemented contract;
- `2`: invocation/precondition/configuration error;
- `3`: schema/CNJ/JSON contract failure;
- `4`: DataJud returned a non-success HTTP status during capture;
- `5`: network/DNS/HTTP transport failure before a successful response.

On verification success the tool prints the typed observation including the SHA-256 of the exact raw fixture. Verification does not mutate the repository or silently admit the fixture as benchmark evidence.

## Wave F gate

`scripts/test-rjudi-wave-f.ps1` first executes all work that does not require an external DataJud response. It then verifies an authentic fixture only when all of these are provided:

- `RJ_DATAJUD_FIXTURE_PATH`
- `RJ_DATAJUD_EXPECTED_CNJ`
- `RJ_DATAJUD_SOURCE_REFERENCE`
- `RJ_DATAJUD_OBSERVED_AT`

If they are absent, the gate returns BLOCKED (`exit 2`) only after build and contract tests have had a chance to execute.

## Observed external execution blocker

On 2026-09-11 an attempt from the available execution container to contact `api-publica.datajud.cnj.jus.br` failed during DNS resolution before any HTTP request reached CNJ. This is environment/network evidence only. It does not imply DataJud unavailability.

## Explicit nonclaims

Wave F does not demonstrate:

- successful live CNJ API access from the project runtime;
- an authentic DataJud response fixture admitted to the repository;
- completeness/stability of undocumented fields;
- a complete DataJud `LegalCase` adapter;
- deterministic multi-source field precedence between Judit and DataJud;
- refresh execution through DataJud;
- production request rate/backoff/retry policy;
- public API availability SLA;
- legal authorization for any use beyond the CNJ terms of use.
