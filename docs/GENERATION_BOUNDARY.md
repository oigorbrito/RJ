# Structured generation boundary

## Scope

This phase defines the application boundary for a future generation model. No external provider, model SDK, prompt framework, HTTP generation endpoint, or production model implementation is introduced.

The boundary is currently internal to the Application layer and benchmark harness. The production API exposes deterministic generation context construction only; it does not register `GenerationService`, `IGenerationModel`, `GenerationModelOutput`, claims, or citations as an HTTP transport contract.

The benchmark CLI may invoke an `IGenerationModel` through `GenerationBenchmarkRunner`, but it serializes the benchmark report through `GenerationBenchmarkJson`; it does not serialize `GenerationModelOutput` directly as a CLI artifact. Therefore no transport DTO for model output is introduced until an actual public generation surface exists.

## Input contract

`IGenerationModel.GenerateAsync` accepts exactly one application input: `GenerationContext`.

The interface does not accept raw documents, arbitrary prompts, caller-provided evidence, database connections, retrieval services, or unverified text. Context construction remains a separate deterministic stage.

`GenerationService` refuses to invoke the model when `GenerationContext.Items` is empty.

## Output contract

A model returns `GenerationModelOutput`, containing only structured `GenerationClaim` entries. There is deliberately no free-text answer field outside claims.

Every claim contains:

- non-empty claim text;
- one or more `GenerationCitation` references.

A citation identifies an evidence unit by:

- `documentId`;
- `contentSha256`;
- `startOffset`;
- `length`.

## Post-model validation

`GenerationService` validates every returned claim before accepting the output.

A citation is valid only when an item in the exact supplied `GenerationContext` has the same document identifier, content hash, start offset, and length.

The service fails closed when:

1. generation is attempted without evidence;
2. the model returns no structured output;
3. a claim is null or blank;
4. a claim has no citation;
5. a citation is null;
6. a citation does not exactly match supplied context evidence.

The service does not repair, guess, broaden, or synthesize citations after generation.

## Deterministic fake

The focal test suite uses an in-process deterministic implementation of `IGenerationModel`. Its purpose is to prove the boundary contract without introducing an external dependency.

The fake captures the exact `GenerationContext` object it receives and returns a predetermined `GenerationModelOutput`.

## Acceptance evidence

The minimum evidence for this phase is:

1. the model interface accepts only `GenerationContext` plus cancellation;
2. the exact context object reaches the fake model;
3. a correctly cited claim is accepted;
4. an uncited claim is rejected;
5. a citation outside the supplied context is rejected;
6. empty context prevents model execution;
7. the production API exposes no generation-model endpoint and does not serialize `GenerationModelOutput` as HTTP;
8. the benchmark CLI serializes benchmark reports rather than `GenerationModelOutput` directly;
9. no public generation transport DTO is created before a concrete transport requirement exists;
10. prior generation-context, citation, retrieval, ingestion, persistence, domain, and architecture gates remain green.

Missing runtime, runner, database, or local checkout is not PASS. It is `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
