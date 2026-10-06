# OpenAI generation adapter

## Scope

RJudi can execute generation through `IGenerationModel` using either the deterministic model or the OpenAI provider.

The API runtime now selects the provider explicitly through `RJ_GENERATION_PROVIDER`.

Supported runtime values:

- unset or `deterministic` -> `DeterministicProcessSummaryModel`;
- `openai` -> `RJ.Infrastructure.Generation.OpenAiGenerationModel`;
- any other value -> startup fails closed.

This provider selection is a `PROJECT_DECISION`; the fail-closed behavior is used to prevent an unknown or incomplete configuration from being presented as successful generation.

## OpenAI configuration

Set all three variables locally before starting the API:

```text
RJ_GENERATION_PROVIDER=openai
RJ_GENERATION_MODEL=<model-id>
OPENAI_API_KEY=<secret>
```

The API key must not be committed to source control, `appsettings.json`, test files, docs, screenshots, gate artifacts, or chat transcripts.

`RJ_GENERATION_MODEL` is the actual provider model identifier used in the OpenAI Responses API request. The repository does not prescribe a model identifier as an empirical conclusion.

## Runtime boundary

The OpenAI adapter receives only `GenerationContext` data from the existing generation pipeline. Its prompt contains the query plus the supplied evidence identifiers, hashes, positions, and excerpts.

Structured output contains:

- `abstained`;
- `abstention_reason`;
- claims;
- citations containing `documentId`, `contentSha256`, `startOffset`, and `length`.

The adapter rejects a citation unless it exactly matches evidence supplied in the `GenerationContext`. The normal RJudi validator remains downstream of model execution.

There is no silent automatic fallback from `openai` to `deterministic`. A request configured for OpenAI must either execute OpenAI successfully or fail. This prevents deterministic output from being mislabeled as generative-AI output.

## Verification

Local provider/configuration and citation-boundary tests are executed without a real provider credential.

A live OpenAI claim requires an execution with network access plus locally configured `OPENAI_API_KEY` and `RJ_GENERATION_MODEL`. Missing credentials or unavailable network are `BLOCKED`, not `PASS`.

The canonical live MVP gate is:

```powershell
.\scripts\test-rjudi-mvp-wave-3.ps1
```

The gate must not print or persist the API key.

## Failure mode

The API/provider fails closed when:

- `RJ_GENERATION_PROVIDER` has an unsupported value;
- provider `openai` is selected but `RJ_GENERATION_MODEL` is missing;
- provider `openai` is selected but `OPENAI_API_KEY` is missing;
- the provider returns malformed structured output;
- a citation references evidence outside the supplied generation context;
- HTTP/provider execution fails.

## Scope of evidence

Passing the live gate demonstrates the configured model can execute the scoped RJudi process-summary path for the gate fixture while preserving the citation contract. It does not establish comparative model superiority, production accuracy, or generalization to other legal corpora. Such claims require a pre-specified empirical comparison and admitted corpus under the repository empirical harness.
