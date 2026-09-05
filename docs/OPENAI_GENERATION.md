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
RJ_GENERATION_MODEL=<model-id>
OPENAI_API_KEY=<secret>
```

The API key must not be committed to source control, `appsettings.json`, test files, or docs with real values.

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

The adapter uses only the benchmark `GenerationContext` and the structured output contract. It does not use guidelines, oracle text, expected claims, or `model_answer` content as generation input.

## Demo vs real validation

- `oab-rulingbr-generation-challenger-v1` is a local deterministic challenger for offline validation.
- `openai-oab-rulingbr-v1` is the real-model challenger path and remains blocked until provider credentials and network access are available.

## Failure mode

The adapter fails closed when:

- `RJ_GENERATION_PROVIDER` is not `openai`;
- `RJ_GENERATION_MODEL` is missing;
- `OPENAI_API_KEY` is missing;
- the provider returns malformed structured output;
- a citation references evidence outside the supplied generation context;
- HTTP/provider execution fails.
