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

## Corrected execution and acceptance

Head 44f790e2a76ccb134abbb84140d49ae109c14f45; run 37514162976.

| Operation | Job | Executed result |
| --- | --- | --- |
| Runner smoke | 112442668715 | PASS, actual steps executed |
| Full Release build, PostgreSQL migrations twice and suite | 112442700641 | 410 total, 407 succeeded, zero failed, three real-corpus tests NOT_EXECUTED |
| Wave J generation self-test | 112442700582 | 15 focused tests, zero failed/skipped; two raw observations; WAVE_J_GATE=PASS |
| Wave K retrieval self-test | 112442700522 | 17 focused tests, zero failed/skipped; WAVE_K_GATE=PASS |

Both materialization jobs explicitly printed the head above and ran the existing canonical scripts unchanged. Production code and the two-second readiness timeout were not modified. The full-suite HTTP readiness assertion passed under bounded module concurrency. This supports the scheduling correction in this environment; it does not establish a production latency guarantee or prove the sole cause of the earlier timeout.

Wave J report SHA-256: 313b518d0d46589d4853f0939de1bccb26d551e810e6e2895fe82158f184f735.
Wave J policy SHA-256: 12a600b4d813d597260dc50a8c440db337d6eaaca2561f8e9567e16bfdfe03d7.
Wave J configuration SHA-256: e8c34471f3e5a4ab058857dec7abcdbcd78efa27b037816952cfc9eec69c2c1b.
Wave K report SHA-256: 819ca531f50b0f581b0980cddfe17a3b4c903dd703a8e64dee87d7d25dae2695.
Wave K policy SHA-256: 32469d30588e62535327f9f369a8d12b67f2c40c39787fc081f25493637b1004.
Wave K configuration SHA-256: 1e70ee135a50df71256abb6bace75b70bf07c5aef8a47a7649d3060a2f9b4991.

DOCUMENTED: frozen protocol and raw execution identities.
IMPLEMENTED: existing native Actions matrix, exact-head checkout and bounded MTP module concurrency.
EXECUTED: jobs and counts above.
ACCEPTED: synthetic materialization mechanics on the corrected head, with prior failure retained.

Temporary self-test directories were removed by the scripts' finally blocks. No fixture PR or branch deletion is required; this is an implementation PR. Real EVAL-010 corpus/oracle admission, real attachment artifacts, authentic DataJud response evidence and live-provider execution retain their independent external gates. Neither Linux hosted execution nor synthetic fixtures qualify Windows gates or empirical treatment promotion.
