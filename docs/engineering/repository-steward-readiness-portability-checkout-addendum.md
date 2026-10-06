# RJ readiness portability protocol — prospective checkout addendum v1.1

This addendum is frozen before changing the test workflow or executing the changed workflow. The original protocol and source results remain intact.

## Triggering observation

Initial integration head `b19a874c22131b9b4dce3e746019223ef270f24b` had hosted classifier run `37471250009`, job `112295022025`. Both matrix markers passed, but actual checkout was GitHub merge commit `1a42af6201dbf3885fe4ba2560eb03d2a59270f3`, whose parents are initial target main and that PR head. Its tree `fce68b8ef6ceb5615df4eab35f53dd2919e81abc` equals the implementation-head tree. This is executed merge-ref synthetic evidence; do not call it literal head-checkout execution.

## Minimal justified adaptation

The source test workflow labels its checkout “pull request head” but the default checkout ref on a pull_request event is the merge ref. Change only that checkout configuration to:

```yaml
with:
  ref: ${{ github.event.pull_request.head.sha }}
```

Keep both classifiers, their tests and the issue_comment observer byte-identical to source pin `ceea10da9940f202aa6f134de65324d4d2030894`. The test workflow has only contents:read and runs repository-provided Bash; this adds no authority or policy engine. The source contract, application CI, native gates, exclusion semantics and report-only scope remain unchanged.

## Prospective gate

Before acceptance, inspect the new hosted classifier log and require actual checkout equal to the new exact integration head plus both matrix PASS markers. Record run/job/head and the earlier merge-ref result separately. Application CI must pass on the current integration head before activation. If it fails, preserve a concrete unmerged integration and leave native RJ portability NOT_EXECUTED / NOT_ACCEPTED.

This is a REUSE -> ADAPT step limited to execution identity. It does not retroactively promote the initial merge-ref result to exact-head PASS.
