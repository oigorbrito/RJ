# Repository steward readiness — RJ portability protocol

## Frozen prospective scope

DOCUMENTED = YES
IMPLEMENTED = NO (at freeze)
EXECUTED = NO (at freeze)
ACCEPTED = NO (at freeze)

The owner selected `oigorbrito/RJ` for controlled qualification of the NDV readiness observer. This is repository observer portability, not RJ application readiness, release or project closure.

Source repository: `oigorbrito/NDV`.
Source pin: `ceea10da9940f202aa6f134de65324d4d2030894`.
Target initial main: `ebd84a36efbcb8a6832e64c5d40e4d272ce39706`.

REUSE -> ADAPT -> WRAP -> FORK -> BUILD CUSTOM.
Decision: reuse the five existing source files byte-for-byte. Their repository context comes from GitHub event fields; no owner/repository literals, custom policy engine or LLM parser are added.

## Positive contract and authority

Reuse the frozen v2 six-field rule and existing exclusion precedence:

```text
OPEN && !isDraft && CLEAN && MERGEABLE && checks=SUCCESS
&& reviewDecision in {APPROVED, explicit-null/NONE}
=> READY_FOR_MERGE_CANDIDATE
```

Malformed, missing, errored, unknown or incomplete input must not produce a candidate. Preserve the source extraction adapter and matrix unchanged. Only report; the workflow has read permissions and cannot merge, approve, request review, rerun checks, close issues, release, change rules or delete branches.

The observer remains an `issue_comment` workflow from main, outside the measured PR head check rollup. Commands: `/readiness-observe` and `/readiness-observe-v2` select v2; `/readiness-observe-v1` preserves v1 reproduction. No behavior changes are justified by target-specific outcomes.

## Prospective gates

1. Freeze this protocol in a commit before the source-file implementation commit and any target test result.
2. Verify byte identity of both classifiers, matrix script and both workflows against the source pin. Parse workflows, check shell syntax and execute the unchanged synthetic matrix. Synthetic PASS does not establish native portability.
3. Execute the hosted classifier job on the exact integration PR head; inspect its actual steps and both matrix PASS markers. Keep the application CI result separate.
4. Require successful exact-head application CI before the authorized integration merge. A historical baseline FAIL does not permit ignoring a new head failure.
5. After integration is on main, observe a minimal documentation fixture with successful target checks, recording exact implementation/head/run/job/native OBSERVATION. Require v2 READY_FOR_MERGE_CANDIDATE on the settled native six-field inputs.
6. Convert that same-head fixture to draft, observe again and require NOT_READY_DRAFT, even if native mergeStateStatus remains CLEAN. A native check failure/pending observation encountered during execution must remain fail-closed and be recorded, but cannot replace the positive gate.
7. Close only the experimental fixture without merge; retain branches. Inspect all live evidence before accepting RJ portability. Installation or workflow success alone is not PASS.

No repeat of every NDV native fixture is required by this limited portability protocol. NDV review-veto, UNKNOWN, BEHIND and BLOCKED evidence remains source-repository evidence. Target-specific unobserved enums or policy cases remain NOT_PROVEN.

## Baseline and substantive gate

RJ rulesets read `[]`; branch main reports protected=false. This is not policy qualification.

Existing main run `36369344270`, job `108762156192`, on `ebd84a36efbcb8a6832e64c5d40e4d272ce39706` executed checkout, restore, build, database migration and Test. Test failed. Runner smoke job `108762136496` succeeded. This is an executed application/test failure, not unavailable runner infrastructure.

Prepare the observer independently and execute its synthetic gate. If application CI still fails on the integration head, leave the integration unmerged and native target gates NOT_EXECUTED. Do not weaken CI or expand into unrelated application fixes to claim observer acceptance. Record the failure with actual logs for a concrete next decision.

## Results

Later implementation, hosted and live results belong in a separate results document. This frozen protocol is not rewritten to fit outcomes.
