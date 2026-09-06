# OpenAI generation adapter

## Scope

This repository can optionally execute a real external generation model through `IGenerationModel` using the OpenAI provider.

This path is separate from the deterministic demo and challenger models:

- `harness-selftest-v1`
- `oab-bench-demo-v2`
- `oab-rulingbr-generation-challenger-v1`
- `openai-oab-rulingbr-v1`

## Required configuration

Set the provider explicitly:

```text
RJ_GENERATION_PROVIDER=openai
RJ_GENERATION_MODEL=<provider-model-id>
OPENAI_API_KEY=<secret>
```

`openai-oab-rulingbr-v1` is the internal RJ benchmark adapter id. `RJ_GENERATION_MODEL` is the provider model sent to the OpenAI Responses API.

The API key must not be committed to source control, `appsettings.json`, test files, docs, logs, or prompts.

## Execution

Example:

```text
dotnet run --project src/RJ.BenchmarkCli --no-restore -- ^
  --git-commit <git-sha> ^
  --runtime ".NET 10" ^
  --model-id openai-oab-rulingbr-v1 ^
  --model-config "openai-response-schema" ^
  --seed 0 ^
  --output %TEMP%\rj-openai-benchmark.json
```

`--seed` remains benchmark metadata and is not sent to the provider.

The adapter uses only the benchmark `GenerationContext` and the structured output contract. It does not use guidelines, oracle text, expected claims, or `model_answer` content as generation input.

## Diagnostics

Set `RJ_BENCHMARK_DIAGNOSTICS=1` to emit a sanitized `OPENAI_DIAGNOSTIC` line to stderr when the adapter fails.

Failure stages are:

- `HTTP`
- `RESPONSE_PARSE`
- `STRUCTURED_OUTPUT_EXTRACTION`
- `JSON_DESERIALIZATION`
- `LOCAL_VALIDATION`
- `RESULT_CONSTRUCTION`
- `OTHER`

Safe diagnostic fields include the stage, inner exception type, HTTP status, provider error code/message, exception message, provider model, endpoint, failure class, response object type, output item count, content item types, and whether output text or structured JSON was present.

Diagnostics must not print the API key, Authorization header, request body, full provider response body, or other secret headers. Free-text diagnostic values are normalized to one line and bounded in length.

## Demo vs real validation

- `oab-rulingbr-generation-challenger-v1` is a local deterministic challenger for offline validation.
- `openai-oab-rulingbr-v1` is the real-model challenger path and remains blocked for a live smoke in environments without provider credentials/network access.

## Failure mode

The adapter fails closed when:

- `RJ_GENERATION_PROVIDER` is not `openai`;
- `RJ_GENERATION_MODEL` is missing;
- `OPENAI_API_KEY` is missing;
- the provider returns malformed structured output;
- a citation references evidence outside the supplied generation context;
- HTTP/provider execution fails.
