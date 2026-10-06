# Automatic readiness observation — executed evidence

## Decision and scope

Automatic native CI-completion observation is ACCEPTED_PASS in oigorbrito/RJ for READ/REPORT. No PR comment or rules/review setup is required for this operating profile. The observer does not merge, approve, close, release or write repository policy.

#39-41 policy/review fixtures were closed unmerged at the user's request. Their native outcomes remain NOT_PROVEN and optional, not mandatory activation gates. Historical evidence is preserved in repository-steward-readiness-concurrent-native-evidence.md.

## Documented / implemented / executed / accepted

- DOCUMENTED: prospective protocol frozen at 3b55c63fddaf6d9d245689f1dd0bf934943dcbc4 before implementation.
- IMPLEMENTED: #44 head 554e0d73e64cccb308f08bf750add7d44abb7a94, merged activation 424a7d2fcefc28bedec4504a25531631e2262e2a.
- EXECUTED: exact-head association matrix and unchanged v1/v2 classifier matrices, run 37503494177 job 112406164643. PASS markers verified in logs, exact head checkout verified.
- EXECUTED: implementation application CI run 37503494045, runner-smoke 112406165582 / build-test 112406219939. 168 total, 165 succeeded, zero failed, three real-corpus tests skipped. Those three remain NOT_EXECUTED, not corpus PASS.
- EXECUTED / ACCEPTED: native workflow_run automatic trigger and exact-head association, run 37503945699 job 112407710689, after fixture CI run 37503768614 completed. The observer produced a report-only candidate on the current fixture head.

## Native fixture identity

PR #45, branch readiness-fixture/rj-automatic-trigger, head 20f05eda56edad23a916ab8a72d4fdd1438ee43f, base main. CI jobs runner-smoke 112407103953 and build-test 112407135026 completed successfully. Observer code ran from 424a7d2fcefc28bedec4504a25531631e2262e2a on the default branch.

```text
2026-10-06T17:29:36.0393494Z ASSOCIATION source_run=37503768614 source_head=20f05eda56edad23a916ab8a72d4fdd1438ee43f pr=45
2026-10-06T17:29:36.5763341Z OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=SUCCESS base=main head=readiness-fixture/rj-automatic-trigger head_sha=20f05eda56edad23a916ab8a72d4fdd1438ee43f decision=READY_FOR_MERGE_CANDIDATE protocol=v2
```

REST checks on the fixture head contained only runner-smoke and build-test, both SUCCESS. Observer was absent from the head rollup. REST comments on #45 were empty: no command/comment initiated observation. No manual dispatch or rerun. Fixture closed without merge after acceptance; head unchanged, branch retained. Push-main workflow completion observer run 37503939033 was intentionally skipped because this scope targets PR CI completion only; skipped is not PASS.

## Reuse and fail-closed boundaries

GitHub-native workflow_run after existing ci completion; GitHub-native Commit.associatedPullRequests; unique current OPEN PR at exactly the triggering CI head. Missing/closed/stale/ambiguous/incomplete association cannot produce a candidate. Readiness head is rechecked after association. Default-branch code only; no PR checkout, CI artifacts or write permissions in the observer. Original v2 readiness classifier is unchanged. Association guard failures were tested synthetically; this does not claim all native cases were observed.

Reports appear in the observer Actions job summary and logs, automatically after PR CI completion. They are point-in-time observations, not merge authorization or continuous review/policy monitoring. Manual observation commands remain optional for compatibility. This qualification applies to RJ's ci workflow; other repositories need their own workflow-name mapping and native installation evidence. Provider, real corpus, project/release acceptance are outside this qualification.
