# Canonical controlled-fixture gates

Source frozen at 66eeb94fbe4629a784bc5b1fbbe17da212c945fe.

Execute the existing M1, Wave E (including Wave D regression) and local-MVP PowerShell scripts in independent hosted Linux jobs, each with its own disposable PostgreSQL service and literal PR-head checkout. Reuse scripts as-is before considering adaptation. These are controlled-fixture engineering gates; executing them does not qualify Windows, real-corpus admission or production.

Acceptance requires actual script execution, an exact printed head, exit zero and inspection of focused test counts/skips. The wrapper's CANONICAL_GATE_EXECUTED_PASS marker describes successful script completion only. It cannot promote skipped real-corpus tests. Failures stay recorded and block merge until resolved; runner setup failure is separate from product/test failure.

No provider credentials, corpus-admission manifest or external API is configured. The existing steward remains read/report only. Hosted service containers and temporary test databases are discarded by runner/script cleanup. No project closure template is activated.

## Executed evidence — 2026-10-06

Head 9710d32ae42013b24c9d4cc828da5d3fb80d0cac; run 37516203225.

| Canonical gate | Job | Observed results |
| --- | --- | --- |
| M1 | 112449652001 | 111 domain + 54 API tests; zero failed/skipped; exact-head completion observation |
| Local MVP | 112449651978 | 25 controlled-fixture tests; zero failed/skipped; exact-head completion observation |
| Wave E including D/M1 regression | 112449651961 | 6 maintenance + 8 scheduler + 111 M1 domain + 54 M1 API + 15 PostgreSQL schema/persistence + 12 HTTP + 1 maintenance ordering; zero failed/skipped; exact-head completion observation |
| Full Release suite | 112449651819 | 410 total, 407 succeeded, zero failed, three real-corpus skips NOT_EXECUTED |

All seven jobs in the run executed and succeeded; the existing J/K materialization jobs remain successful. Exact head and clean worktree were printed before every canonical gate. The scripts themselves are unchanged. The Wave E log confirms that nested D returned and the maintenance ordering test executed afterward; no nested exit silently bypassed the final step.

DOCUMENTED: protocol and execution identities.
IMPLEMENTED: independent native Actions matrix with disposable PostgreSQL services.
EXECUTED: actual script steps/counts above.
ACCEPTED: controlled-fixture M1/local-MVP and PostgreSQL D/E mechanics in this hosted Linux environment only.

The focused gate counts overlap each other and the full suite; do not sum them as unique tests. Service containers and temporary databases were cleaned up. Real-corpus tests, external evidence, provider quality, Windows execution and project closure are not promoted by these results.
