# Synthetic materialization execution protocol

Frozen source: 08ed1661f7c9375f4d52cd2c9e67e789e82a2d59.

The current full-suite CI does not by itself execute the Wave J/K canonical CLI self-tests. Add two independent jobs to the existing ci workflow and run the existing scripts with PowerShell on the hosted Linux runner. Check out the literal PR head and print it before execution. Preserve each gate's hashes, materialization assertions and temporary-fixture cleanup.

Acceptance requires actual job steps, zero exit status and the respective WAVE_J_GATE=PASS / WAVE_K_GATE=PASS marker on the same head. A workflow present, a skipped job or an earlier PASS is insufficient. Any failure must be inspected and corrected before merge.

Scope is synthetic generation/retrieval observation materialization mechanics. It does not admit a real corpus, validate live providers, promote treatments, qualify Windows execution or close the project. No external provider credentials are configured. The repository steward remains read/report only.

## First execution, retained without promotion

Head db1284dc916161d17209b61d1e67991ff77ac67a; run 37513836703.
Wave J job 112441584032: 15 focused tests passed, two observations materialized and WAVE_J_GATE=PASS.
Wave K job 112441584003: 17 focused tests passed and WAVE_K_GATE=PASS.
Both printed the literal head and completed temporary-fixture cleanup.

The full-suite job 112441583990 failed: 410 total, 406 passed, one failed, three real-corpus skips. Health_endpoints_report_live_and_ready received 503 rather than the required 200. The request lasted 2017 ms and PostgreSQL logged cancellation of the schema-invariant query by the client. HealthEndpoint retains its two-second bound. This is an executed failure, not a merge acceptance.

Concurrent test modules contend for the same PostgreSQL server, including temporary database creation/destruction. Resource contention is a plausible explanation, not a proven sole cause. Use the native .NET 10 MTP --max-parallel-test-modules 1 option to bound module concurrency in the full-suite CI, keeping every test and assertion. Production timeout and code stay unchanged. Independent hosted materialization jobs remain parallel. Reference: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp
