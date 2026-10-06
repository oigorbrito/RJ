# Canonical controlled-fixture gates

Source frozen at 66eeb94fbe4629a784bc5b1fbbe17da212c945fe.

Execute the existing M1, Wave E (including Wave D regression) and local-MVP PowerShell scripts in independent hosted Linux jobs, each with its own disposable PostgreSQL service and literal PR-head checkout. Reuse scripts as-is before considering adaptation. These are controlled-fixture engineering gates; executing them does not qualify Windows, real-corpus admission or production.

Acceptance requires actual script execution, an exact printed head, exit zero and inspection of focused test counts/skips. The wrapper's CANONICAL_GATE_EXECUTED_PASS marker describes successful script completion only. It cannot promote skipped real-corpus tests. Failures stay recorded and block merge until resolved; runner setup failure is separate from product/test failure.

No provider credentials, corpus-admission manifest or external API is configured. The existing steward remains read/report only. Hosted service containers and temporary test databases are discarded by runner/script cleanup. No project closure template is activated.
