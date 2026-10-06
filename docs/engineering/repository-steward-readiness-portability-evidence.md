# Repository steward readiness — RJ executed portability evidence

## Classification

```text
DOCUMENTED                  = FROZEN_PROTOCOL + PROSPECTIVE_CHECKOUT_ADDENDUM
IMPLEMENTED                 = YES / SOURCE_REUSE_WITH_CHECKOUT_ADAPTATION
LOCAL_MATRIX                = EXECUTED_PASS / SYNTHETIC
HOSTED_EXACT_HEAD_MATRIX     = EXECUTED_PASS / SYNTHETIC
APPLICATION_HERMETIC_CI      = EXECUTED_PASS / 165_SUCCEEDED_0_FAILED_3_REAL_CORPUS_SKIPPED
NATIVE_PENDING_EXCLUSION    = EXECUTED_PASS / RJ
NATIVE_NO_REVIEW_CANDIDATE   = EXECUTED_PASS / RJ
NATIVE_SAME_HEAD_DRAFT       = EXECUTED_PASS / RJ
OBSERVER_PORTABILITY        = ACCEPTED / LIMITED_READ_REPORT_SCOPE
DEFAULT_COMMAND             = v2 / EXECUTED_PASS
REAL_CORPUS_TESTS            = NOT_EXECUTED / 3_EXPLICIT_SKIPS
REAL_PROVIDER               = NOT_EXECUTED
RJ_PROJECT_OR_RELEASE_PASS  = NOT_CLAIMED
```

Scope is the frozen NDV-to-RJ observer portability protocol, not project closure. Installation, configuration, merge and successful workflow conclusion alone did not qualify this result.

## Frozen identity and source reuse

- Source: `oigorbrito/NDV`, pin `ceea10da9940f202aa6f134de65324d4d2030894`.
- Target initial main: `ebd84a36efbcb8a6832e64c5d40e4d272ce39706`.
- Original protocol freeze: `16a75d24935f6de4e3fd5b720a6493f04a42fe89`, before implementation `b19a874c22131b9b4dce3e746019223ef270f24b`.
- Checkout addendum freeze: `58c40808d34511104abc0a0b5bcd68a0eb7988f3`, before adaptation `0ab0ba0a01b3432bc4f6a78a0ffaa4531f985c0b`.
- Final integration head: `f9463db2c0e209bb7416153640604a115abe6371`.
- Integration PR #34 merged as `2d4df48fa8fcc88ee9687524e5416a0451092fb0`, the implementation used for all native observations below.

Both classifiers, their matrix script and the issue_comment observer are byte-identical to the pinned source. Only the classifier test workflow adapts checkout to explicit `github.event.pull_request.head.sha`. Repository identity comes from GitHub event context; no target-specific policy engine or LLM parser was introduced.

Verified source blob identities:

| File | Source/target blob SHA |
| --- | --- |
| v1 classifier | e67cfe3da3c423f14ed677fae4be401f97fd78ad |
| v2 classifier | 0fa164b6394fe7282f180fefb6ec6fee1938c7ce |
| matrix script | a690e5f7ef5c62cb1c90846ff7588ba12a1fd3d1 |
| observer workflow | 490a1bc11700cff4c862fbc35705ffedffad7668 |

## Synthetic execution and literal head identity

Local Bash syntax, both unchanged matrices and workflow YAML parsing passed. The adapted checkout ref and contents:read permission were also asserted. Local .NET compilation was NOT_EXECUTED because the editing container has no SDK.

Initial hosted classifier run `37471250009`, job `112295022025`, passed both matrices but actually checked out merge ref `1a42af6201dbf3885fe4ba2560eb03d2a59270f3`; its tree matched initial head `b19a874c22131b9b4dce3e746019223ef270f24b`. This remains merge-ref evidence, not literal head-checkout PASS.

After the prospective addendum:
- Run `37471501230`, job `112295874123`: actual checkout `0ab0ba0a01b3432bc4f6a78a0ffaa4531f985c0b`; both matrix markers PASS.
- Final integration run `37473743971`, job `112303643370`: actual checkout `f9463db2c0e209bb7416153640604a115abe6371`; `CLASSIFIER_MATRIX=PASS` and `CLASSIFIER_V2_MATRIX=PASS`.

These are synthetic execution results, not substitutes for live native target observations.

## Application CI prerequisite

The initial observer PR reproduced nine actual application test failures: run `37471501176`, job `112295920417`. Runner, restore, build and migrations executed; this was not a missing-runner blocker.

Owner continuation authorized separate repair PR #35. It corrected adapter exception/report contracts and synthetic fixtures, preserved actual runtime provenance, and removed developer-specific Windows corpus dependencies from default CLI tests. The original three real-corpus tests retain their case-count assertions under explicit opt-in and supplied roots; default skips are NOT_EXECUTED, not real-data PASS. See the dedicated prerequisite repair document and PR #35 results.

Repair head `033f6df83f63b8e9852e6b6121d93e0cedb899af`, run `37473382046`, runner job `112302387806`, build-test job `112302436286`: 168 total, 165 succeeded, zero failures, three explicit real-corpus skips. It merged as `ec8cb150a808bb53ffbd146a3acbb7eaf969f840`.

Final observer integration CI on head `f9463db2c0e209bb7416153640604a115abe6371`: run `37473743986`, runner job `112303649279`, build-test job `112303701927`, same counts and successful restore/build/migrations/test. Actual application checkout was merge ref `2eff9511143ee90e18957158fdacb5ee88cca6ed`; its tree was independently verified equal to the exact integration-head tree. The classifier separately used the literal head.

No CI command, error exit or test failure was ignored. Real corpus admission/quality and RJ-BLK-003 remain unresolved.

## Native target fixture

Fixture PR #36 had exact unchanged head `22bee10a8f087e655497b42932f08b656441c36a`, base main `2d4df48fa8fcc88ee9687524e5416a0451092fb0`. A single documentation file was added; it was never merged.

Fixture CI run `37474091872`, runner job `112304847120`, build-test job `112304897053`: actual successful execution, 168 total, 165 succeeded, zero failures, three real-corpus skips. REST check runs on the fixture head contained only runner-smoke and build-test. The observer ran from main via issue_comment and did not enter that rollup.

### Actual pending checks

Trigger comment `6017734457`; run `37474091808`, job `112304845856`:

```text
OBSERVATION state=OPEN draft=false mergeStateStatus=UNSTABLE mergeable=MERGEABLE reviewDecision=NONE checks=PENDING base=main head=readiness-fixture/portability-open-draft head_sha=22bee10a8f087e655497b42932f08b656441c36a decision=NOT_READY_CHECKS protocol=v2
```

Actual target checks were in progress. Fail-closed pending behavior passed without head mutation.

### Settled candidate

Trigger comment `6017774745`; run `37474389558`, job `112305886688`:

```text
OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=SUCCESS base=main head=readiness-fixture/portability-open-draft head_sha=22bee10a8f087e655497b42932f08b656441c36a decision=READY_FOR_MERGE_CANDIDATE protocol=v2
```

This satisfies the frozen positive six-field rule on the target. NONE came through the unchanged typed native adapter; it is not an inference that repository policies are absent. The candidate does not authorize merge.

### Same-head draft

After converting fixture #36 to draft without changing its head, trigger comment `6017785799`; run `37474472618`, job `112306172190`:

```text
OBSERVATION state=OPEN draft=true mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=SUCCESS base=main head=readiness-fixture/portability-open-draft head_sha=22bee10a8f087e655497b42932f08b656441c36a decision=NOT_READY_DRAFT protocol=v2
```

Draft and CLEAN coexisted on RJ too. The observer correctly excluded readiness using isDraft. Each observation job executed checkout, native query and report-only assertion; actual logs were independently inspected before classification.

## Cleanup, authority and acceptance boundary

Fixture #36 was closed without merge at `2026-10-06T13:54:47Z`; its branch was retained. No fixture review, repository rule mutation, rerun, release or branch deletion occurred.

The prospective freeze, source identity, local/hosted synthetic, application-CI, native candidate, same-head draft and cleanup gates were satisfied within the frozen scope. Observer portability from the pinned NDV implementation to RJ is accepted for READ/REPORT with this checkout adaptation. Default `/readiness-observe` and explicit v2 route to v2; explicit v1 preserves historical reproduction.

RJ rulesets initially read `[]`, and main reported protected=false. That is repository configuration evidence, not branch-policy PASS. Native review veto, APPROVED, REVIEW_REQUIRED, conflict, BLOCKED, BEHIND and UNKNOWN are not requalified on RJ by these measurements; source-repository outcomes remain source evidence and matrix outcomes remain synthetic.

The workflow still cannot merge, approve, request review, rerun checks, close issues, release, change rules or delete branches. Operator merges of reviewed integration/repair/documentation PRs are separate from steward authority. No project-closure checklist was instantiated.
