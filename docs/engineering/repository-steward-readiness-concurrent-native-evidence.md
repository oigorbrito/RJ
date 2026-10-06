# RJ native readiness concurrent qualification

## Scope and source

Independent fixtures opened without waiting for preceding CI. Observer source at start: f725e733130e604cd7ed7d720aa4a07231fdb7e9. No classifier or workflow changes. No main policy changes. Fixture PRs never merge.

DOCUMENTED / IMPLEMENTED fixtures do not establish native PASS. Observation logs determine EXECUTED / ACCEPTED.

| Case | PR | Exact head | Status |
|---|---|---|---|
| conflict | #38 | dcb5dffc9943cf0eaabbdcd3faadf11fe5e1f340 | EXECUTED_PASS; closed unmerged |
| behind | #39 | 9c19741f315c0f2d1861aaada5692b5e8e8ad1a3 | PENDING human gate |
| blocked | #40 | 78bd6040236ffa27e68937740fc3b3a27c8a53ce | PENDING human gate |
| review | #41 | 2d9770ed79948ea885f209ae0f8de0c1cbb78fd1 | PENDING human gate |
| unknown | #42 | db929124c603ed1bbd72a42913eb11a6bd9d3bfe | NOT_PROVEN; closed unmerged |

## Executed observations

Run 37476069874; job 112311676121.

```text
2026-10-06T14:05:39.4845467Z OBSERVATION state=OPEN draft=false mergeStateStatus=DIRTY mergeable=CONFLICTING reviewDecision=NONE checks=NONE base=readiness-fixture/rj-conflict-base head=readiness-fixture/rj-conflict-head head_sha=dcb5dffc9943cf0eaabbdcd3faadf11fe5e1f340 decision=NOT_READY_CONFLICT protocol=v2
```

Run 37476079141; job 112311707939.

```text
2026-10-06T14:05:43.8300359Z OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=NONE base=readiness-fixture/rj-behind-base head=readiness-fixture/rj-behind-head head_sha=9c19741f315c0f2d1861aaada5692b5e8e8ad1a3 decision=NOT_READY_CHECKS protocol=v2
```

Run 37476091198; job 112311748277.

```text
2026-10-06T14:05:49.4803763Z OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=NONE base=readiness-fixture/rj-blocked-base head=readiness-fixture/rj-blocked-head head_sha=78bd6040236ffa27e68937740fc3b3a27c8a53ce decision=NOT_READY_CHECKS protocol=v2
```

Run 37476102755; job 112311791084.

```text
2026-10-06T14:05:53.1763878Z OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=NONE base=main head=readiness-fixture/rj-review-head head_sha=2d9770ed79948ea885f209ae0f8de0c1cbb78fd1 decision=NOT_READY_CHECKS protocol=v2
```

Run 37476113900; job 112311827205.

```text
2026-10-06T14:05:59.3671268Z OBSERVATION state=OPEN draft=false mergeStateStatus=CLEAN mergeable=MERGEABLE reviewDecision=NONE checks=NONE base=main head=readiness-fixture/rj-unknown-head head_sha=db929124c603ed1bbd72a42913eb11a6bd9d3bfe decision=NOT_READY_CHECKS protocol=v2
```


Conflict native DIRTY / CONFLICTING is accepted for NOT_READY_CONFLICT. Native UNKNOWN was not observed: earliest sample was CLEAN / MERGEABLE; NOT_READY_CHECKS from checks NONE is not UNKNOWN PASS. Do not manipulate classifier input to manufacture native evidence.

## Pending human gates

- #39: active ruleset only refs/heads/readiness-fixture/rj-behind-base, require build-test with strict up-to-date policy. Base advanced to a6f1ba4c4d1ce58e70f8dd535b2ee5db65e10515; head unchanged. Native BEHIND still NOT_PROVEN.
- #40: active ruleset only refs/heads/readiness-fixture/rj-blocked-base, require one approving review. Do not approve fixture. Native BLOCKED / REVIEW_REQUIRED still NOT_PROVEN.
- #41: different human account submits Request changes. Wait for actual check SUCCESS before accepting isolated review-veto behavior. Native review veto still NOT_PROVEN.

No ruleset absence is accepted as policy PASS. No rules writes or reviews were performed by the agent. No observer authority expansion. No corpus/provider/project/release qualification claimed.
