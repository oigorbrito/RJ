# Empirical retrieval and generation selection

Status: selection procedure implemented; treatment promotion remains BLOCKED until the EVAL-010 real corpus is admitted.

## Scope

This procedure operationalizes U15-U17 without inventing a weighted score, arbitrary promotion threshold, post-hoc metric, or provider preference.

The unit of comparison is a paired observation for the same admitted legal process case under baseline and challenger treatments.

## Treatment identities

Retrieval identities are semantic experiment labels, not claims that all candidates are implemented:

- `R0`: current case-scoped PostgreSQL Portuguese lexical FTS baseline using `ts_rank_cd`; implemented in RJ;
- `R1`: vector-only candidate; not demonstrated;
- `R2`: lexical + vector hybrid candidate; not demonstrated;
- `R3`: hybrid + reranking candidate; not demonstrated.

Generation identities:

- `G0`: deterministic process-summary fake; implemented control, not a production model;
- `Gx`: an externally supplied challenger whose exact provider/model/runtime configuration is frozen before execution.

`R1-R3` and `Gx` remain `EMPIRICAL_DECISION_PENDING`. Their labels do not authorize implementation or promotion.

## Pre-registration boundary

Before observing candidate results, the selection manifest must freeze:

- research question;
- EVAL-010 corpus manifest reference and SHA-256;
- exact baseline and challenger IDs;
- immutable configuration artifact reference + SHA-256 for both treatments;
- required metric IDs and direction (`HigherIsBetter` or `LowerIsBetter`);
- non-compensable gate IDs;
- exact git commit;
- exact `.NET` runtime description;
- dependency evidence reference + SHA-256;
- command evidence reference + SHA-256;
- one raw observation artifact reference + SHA-256 per case/treatment.

The entire selection manifest is itself verified by SHA-256 before the decision procedure runs.

## Raw observations

`EmpiricalCaseObservation` retains, per case and treatment:

- execution status: `Pass`, `Fail`, `Blocked`, or `NotTested`;
- pre-specified measurements;
- failed non-compensable gates;
- immutable raw artifact reference and SHA-256.

The referenced raw artifact is typed as `rjudi-empirical-raw-observation-v1`. The verifier parses it and requires exact agreement with the manifest for:

- case id;
- treatment id;
- execution status;
- every measurement key/value;
- failed non-compensable gates.

Therefore a valid file hash alone is insufficient if the manifest copied different metrics from that file.

`NotTested` and `Blocked` are never interpreted as `Pass`.

A case missing from either treatment is not silently dropped. The comparison becomes `Blocked`.

## Hard gates

Non-compensable gates precede numeric comparison. A failure cannot be averaged away.

Decision behavior:

- baseline safe / challenger hard-gate failure -> `KeepBaseline`;
- challenger safe / baseline hard-gate failure -> `SelectChallenger`;
- both have hard-gate failures -> `NoClearWinner`;
- any `Blocked` or `NotTested` observation -> `Blocked`.

The exact gate IDs are frozen by the manifest. For generation these are expected to include the already-defined safety/correctness gates such as exact citations, no raw PII, no oracle leakage, no attachment-content invention, authorized generation context, and validator pass where applicable.

## Pareto rule

When both treatments pass every hard gate, comparison occurs over every required `case × metric` coordinate.

For each coordinate, the metric direction is frozen before results:

- `HigherIsBetter`: challenger value greater than baseline is better;
- `LowerIsBetter`: challenger value lower than baseline is better.

No epsilon or tolerance is invented by this protocol version.

The decision is:

- `SelectChallenger`: challenger is never worse and is better on at least one required coordinate;
- `KeepBaseline`: challenger is never better and is worse on at least one required coordinate;
- `NoClearWinner`: trade-off exists, or all required paired values are equal;
- `Blocked`: required evidence is incomplete or unexecuted.

This is a strict observed-case Pareto rule. It deliberately avoids weighted scores and compensating one case/metric degradation with improvement elsewhere.

## Optional measurements

Metrics marked `Required=false` may be retained for diagnosis but do not participate in the selection decision for this protocol version. Turning an optional metric into an acceptance rule requires a new protocol/manifest version before observing the new comparison results.

## Verifier

Tool:

```powershell
dotnet run --project tools/RJ.EmpiricalSelectionVerifier/RJ.EmpiricalSelectionVerifier.csproj -- `
  <selection-manifest.json> `
  <expected-selection-manifest-sha256> `
  <artifact-root> `
  <executed-git-commit>
```

The canonical Wave I gate supplies `<executed-git-commit>` from `git rev-parse HEAD`.

The verifier checks:

1. exact selection-manifest SHA-256;
2. manifest schema/invariants;
3. manifest git commit equals the actually executed commit;
4. manifest runtime equals `RuntimeInformation.FrameworkDescription` of the verifier process;
5. EVAL-010 corpus manifest artifact hash;
6. baseline configuration artifact hash;
7. challenger configuration artifact hash;
8. dependency-evidence artifact hash;
9. command-evidence artifact hash;
10. every raw observation artifact hash;
11. raw observation contents exactly match the manifest observation;
12. paired case set and required metric completeness;
13. non-compensable gates;
14. Pareto decision.

Exit codes:

- `0`: a complete decision artifact was produced (`SelectChallenger`, `KeepBaseline`, or `NoClearWinner`);
- `2`: comparison is `Blocked` or required external inputs are unavailable;
- `3`: manifest/integrity/schema/runtime/commit evidence is invalid.

A verifier exit `0` with `NoClearWinner` is a valid empirical result and must not be converted into a winner by manual weighting.

## Wave I gate

Canonical command:

```powershell
.\scripts\test-rjudi-wave-i.ps1
```

The gate runs executable local work first:

1. solution build;
2. `EmpiricalSelectionServiceTests`;
3. `EmpiricalSelectionManifestTests`;
4. `EmpiricalRawObservationArtifactTests`;
5. `git diff --check`;
6. resolve exact `git rev-parse HEAD`.

Only after those steps does it require external selection evidence through:

- `RJ_EMPIRICAL_SELECTION_MANIFEST_PATH`;
- `RJ_EMPIRICAL_SELECTION_MANIFEST_SHA256`;
- `RJ_EMPIRICAL_SELECTION_ARTIFACT_ROOT`.

If external evidence is absent, the gate returns `BLOCKED RJ-BLK-003` with exit code `2` after the executable contract work has been attempted.

## Reproducibility classification

- paired same-case comparison: `REPRODUCIBILITY_SUPPORTED`;
- immutable references + hashes for corpus/config/observations/commands/dependencies: `REPRODUCIBILITY_SUPPORTED`;
- manifest commit/runtime verification against the executing environment: `REPRODUCIBILITY_SUPPORTED`;
- raw observation → manifest content binding: `REPRODUCIBILITY_SUPPORTED`;
- pre-specification of metrics and hard gates: `EMPIRICALLY_SUPPORTED` / `REPRODUCIBILITY_SUPPORTED`;
- prohibition on post-hoc acceptance metrics: `DERIVED_FROM_METHOD`;
- strict case×metric Pareto rule: `PROJECT_DECISION` chosen to avoid unsupported weighting;
- treatment labels `R0-R3`, `G0/Gx`: `PROJECT_DECISION` / specification taxonomy, not empirical findings.

## Explicit nonclaims

Wave I does not show that:

- vector retrieval is better than lexical retrieval;
- hybrid retrieval is better than `R0`;
- reranking is required;
- any external generation provider is better than `G0`;
- OpenAI or any other provider is selected for production;
- latency/cost/privacy results exist for treatments not executed;
- the single historical case is sufficient for cross-case or cross-tribunal selection.

Those claims remain blocked until the admitted 30-50 case EVAL-010 corpus and independently reviewed oracle artifacts exist and the paired treatment manifests are executed.
