# Generation evaluation protocol

## Scope

This protocol evaluates structured generation outputs before any real model provider is selected or integrated. Evaluation runs against deterministic fixtures/oracles and does not depend on a provider SDK, network call, or subjective aggregate score.

## Fixture contract

Each `GenerationEvaluationCase` records:

- stable fixture identifier;
- exact `GenerationContext` supplied to the model;
- expected structured claims and their exact citations;
- whether the correct behavior is abstention.

`ExpectedGenerationClaim` is the oracle for an answered fixture. Claim text and citation tuples are compared ordinally and exactly.

## Explicit abstention

`GenerationModelOutput` contains:

- `Abstained`;
- optional `AbstentionReason`;
- structured `Claims`.

A valid abstention requires `Abstained=true`, a non-blank reason, and zero claims. A normal answer requires `Abstained=false`, no abstention reason, and at least one cited claim. `GenerationService` enforces this contract before an output is accepted.

## Metrics

`GenerationEvaluator` reports metrics independently:

### Claim recall

Fraction of expected oracle claims present in the output with exact citation sets.

### Citation validity

Fraction of output citations that exactly identify an item in the fixture's supplied `GenerationContext` by:

- `documentId`;
- `contentSha256`;
- `startOffset`;
- `length`.

### Groundedness

For this deterministic pre-provider benchmark, a claim is grounded only when all of its citations are valid and the complete claim/citation set exactly matches an oracle claim. This intentionally measures reproducible fixture-level support, not semantic similarity or an LLM-as-judge opinion.

## Evaluation result boundary

`GenerationEvaluationResult` is deliberately metric-only. It records:

- `caseId`;
- whether the candidate abstained;
- expected and actual claim counts;
- claim recall;
- citation validity;
- groundedness;
- overall pass/fail.

It does not retain generated claim text, expected claim text, evidence excerpts, citation coordinates, content hashes, abstention reasons, prompts, or the full `GenerationModelOutput`. Those values remain available in the versioned fixture catalog and transient evaluation inputs where they are needed for exact comparison, but are not duplicated into the evaluation result persisted by the benchmark report.

This separation reduces unnecessary legal/model content in benchmark artifacts without weakening the hard-gate evidence: the report still proves the exact candidate/configuration, case identity, metric outcomes, per-case pass/fail state, and any sanitized execution error.

## Non-compensable gates

An answered fixture passes only when all three conditions hold:

- `ClaimRecall == 1.0`;
- `CitationValidity == 1.0`;
- `Groundedness == 1.0`.

No averaging is performed. A perfect result on one metric cannot compensate for a failure on another.

An abstention fixture passes only when the output explicitly abstains with a reason and emits no claims.

## Promotion rule

A real generation model or provider must not be promoted merely because it integrates successfully or produces plausible text. Before promotion it must execute the versioned evaluation fixtures and satisfy every non-compensable gate. Provider/model/version/configuration and raw evaluation results must be recorded for reproducibility when that comparison is introduced.

## Minimum acceptance evidence

1. explicit abstention is representable and validated;
2. exact grounded/cited output passes;
3. invalid citations fail independently of other scores;
4. missing expected claims fail independently of citation validity;
5. expected abstention is evaluated separately from answered cases;
6. `GenerationEvaluationResult` is metric-only and does not duplicate generated/oracle claim text, evidence excerpts, citation coordinates, hashes, or abstention reasons;
7. benchmark persistence therefore records evaluation metrics rather than raw generation/evidence content through this result type;
8. no external model is required to execute the evaluator;
9. prior generation-boundary, generation-context, citation, retrieval, ingestion, persistence, domain, and architecture gates remain unchanged.

Missing runtime, database, CI runner, or local checkout is not PASS. It is `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
