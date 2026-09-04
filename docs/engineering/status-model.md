# Status model

Allowed validation states:

- PASS
- FAIL
- BLOCKED
- NOT_TESTED
- NOT_APPLICABLE

Rules:

- Missing execution is never PASS.
- BLOCKED requires recorded external evidence and an objective unblock condition.
- FAIL means the requirement or test executed and did not meet acceptance criteria.
- NOT_TESTED means execution has not occurred and no external blocker has been established.
- NOT_APPLICABLE means the requirement does not apply to the evaluated scope.
