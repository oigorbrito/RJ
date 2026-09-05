# OAB-Bench demo corpus

## Scope

The repository can consume `C:\Projetos\RJ\oab-bench` as a read-only external demo corpus through the environment variable `RJ_LEGAL_DEMO_CORPUS`.

This demo path is intentionally separate from the real legal validation path:

- `DEMO_LEGAL_VALIDATION` uses the cloned OAB-Bench corpus as a wiring and contract check;
- `REAL_LEGAL_VALIDATION` remains blocked until authorized real corpus/oracle artifacts are supplied.

## Input files used

The demo adapter reads only:

- `data\oab_bench\question.jsonl`
- `data\oab_bench\reference_answer\guidelines.jsonl`
- `data\judge_prompts.jsonl`

The adapter does not use `model_answer` files as gold truth.

## Mapping

The demo adapter maps each `question.jsonl` row into an external benchmark case:

- `statement` becomes the benchmark query and the preserved source excerpt;
- `guidelines.jsonl` becomes the oracle/reference text used to build expected claims;
- `judge_prompts.jsonl` is recorded as methodology/configuration provenance only.

The adapter writes a temporary catalog plus temporary source/oracle snippets for admission. It does not copy the external repository into versioned RJ artifacts.

## Expected outcome

The demo path proves:

- corpus parsing;
- provenance recording;
- SHA-256 capture for the exact artifacts used;
- corpus admission;
- benchmark execution wiring;
- explicit separation from the real corpus blocker.

It does not close `RJ-BLK-003`.
