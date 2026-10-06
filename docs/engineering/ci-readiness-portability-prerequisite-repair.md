# CI repair scope — native readiness portability prerequisite

This repair addresses the executed application gate failure observed while preparing repository steward portability. It does not establish RJ project closure, real legal corpus quality, OpenAI provider qualification or observer portability PASS.

## Triggering executed failure

PR #34 head `0ab0ba0a01b3432bc4f6a78a0ffaa4531f985c0b`, application run `37471501176`, job `112295920417`: runner, restore, build and migrations ran; Test failed 9 of 164. Its source baseline is main `ebd84a36efbcb8a6832e64c5d40e4d272ce39706`. No application CI step is removed or ignored.

## Repairs and evidence boundaries

- The successful OpenAI response fixture claimed citation length 32 for a 22-character supplied excerpt. Correct the fixture to the exact context, retain out-of-context rejection, and add length-bound regressions. No real provider call is needed for these adapter tests.
- JSON parsing failures are exposed as the adapter's InvalidOperationException contract with the original JsonException as inner exception. Failed parsing remains rejection, not fallback output.
- A/B report serialization now uses camelCase consistently with its tested JSON contract. Preserve actual runtime timestamps. Compare every remaining report field for determinism after explicitly validating/removing only runtime from the test comparison.
- Default CLI tests use temporary, clearly synthetic OAB inputs instead of developer-specific Windows corpus paths. Gate-failure exit semantics remain tested.
- Preserve the original three real-corpus tests and their case-count assertions, with portable roots and explicit opt-in `RJ_RUN_REAL_CORPUS_TESTS=1`. Without that opt-in, they are skipped as NOT_EXECUTED, never real-data PASS. Add hermetic read-only catalog/array/JSONL coverage separately; those results are synthetic only.

## Real-corpus lane

Supply `RJ_REAL_OAB_CORPUS` and `RJ_REAL_RULINGBR_CORPUS` pointing to externally admitted snapshots, then set `RJ_RUN_REAL_CORPUS_TESTS=1` and run the normal tests. Enabling the lane with missing or invalid roots fails; no success fallback or automatic download is introduced. The preserved RulingBR assertions expect the original sample count 5 and corpus count 10574. Corpus admission and hashes remain separate requirements.

Default CI success with these real-corpus tests skipped establishes only the hermetic engineering gate. It must not resolve RJ-BLK-003 or claim benchmark/production acceptance. The skip reason appears in actual test output.

## Validation state

At implementation: DOCUMENTED / IMPLEMENTED; no .NET SDK is available in the editing container, so local compilation is NOT_EXECUTED. Hosted restore/build/migration/test must be inspected on the repair PR's exact head before merge. PR results record later run/job and counts separately; successful configuration or PR presence is not PASS.
