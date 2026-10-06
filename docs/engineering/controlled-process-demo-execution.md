# Controlled process demo execution

Frozen source 66eeb94fbe4629a784bc5b1fbbe17da212c945fe. The historical Wave 2 Windows document points to local artifacts unavailable here and is not accepted as current-head evidence.

Reuse the existing five-case canonical script and native .NET TCP listener enumeration to replace the Windows-only Get-NetTCPConnection call. Inspect the port configured by ApiUrl rather than the hardcoded 5001, before database mutation. Keep the occupied-port rejection, restrict the controlled gate to loopback HTTP and restore its demo/URL environment and stop its own process in finally.

Execute two hosted fixtures: occupied configured port 5003 must fail closed before seeding; free configured port 5002 must migrate, seed all five cases, start the real API, check five CNJs/documents and the unknown-CNJ 404, then stop its listener. Verify the result artifact's exact head, clean worktree, five-case count and all step statuses. Generation provider is explicitly deterministic; no external credentials or calls.

Scope: Linux hosted controlled-fixture API execution only. No real corpus, production authentication, provider quality, Windows gate or project closure claim. Prior historical claims are preserved as history. A workflow or successful setup alone is not PASS.

## Initial execution, retained

Head 5b716f5a6540bf832374aacb4ac5a26af906a75f; run 37516383118; job 112450255115.
The occupied configured-port fixture printed OBSERVATION port=5003 occupied=true exit=1 decision=OCCUPIED_PORT_REJECTED_PASS. The canonical child script rejected the occupied port before migration/seed as intended. However, the Actions PowerShell wrapper propagated the expected child's LASTEXITCODE=1 after the assertion completed, failing the step. The five-case positive runtime step did not execute. Overall job = FAIL; no overall PASS.

Correction: exit zero only after verifying BOTH nonzero child status and the exact occupied-port diagnostic and stopping the fixture listener. Unexpected success or another diagnostic still throws. This normalizes an asserted negative fixture outcome, not an ignored test failure.

## Second execution and current-main integration

Head 7c848d9eca47c01ffc67b8a0b72dc1ae727a083b; run 37516575224; controlled-process job 112451456381.
The occupied-port step passed after its asserted-status normalization. The positive step failed before migration at branch metadata capture: literal SHA checkout is detached, git branch --show-current returns no string, and calling Trim on null throws. No five-case runtime PASS is claimed. Other CI jobs succeeded.

Normalize detached branch output to an empty string while keeping the authoritative exact commit. Do not fabricate a branch name or weaken literal-head checkout. Use the owned Process.Kill(entireProcessTree: true) native cleanup so the spawned dotnet run child cannot survive its parent.

Integrate main ec51e96576e30c5d938f29f7eb7aeb859fde4d4b, retaining PR #51's canonical jobs and evidence in addition to this demo job; both PRs independently changed the CI file. This reconciliation preserves all jobs and does not alter steward authority.

The attempted inline cast did not normalize PowerShell's empty native-command output in this runner: head e4ca4de13cf132e1d8c50483489315169b2e72da, run 37517044366, job 112452582006 failed at the same metadata line before migration. Replace it with explicit native output capture and a null branch guard before invoking Trim. Detached branch remains the truthful empty value. This failed attempt is retained, not reclassified as PASS.

## Executed controlled demo — 2026-10-06

Head 5d43de82a4a28d93c82e22e31bc979a8005aceef; run 37517219651; controlled-process job 112453299057.

```text
OBSERVATION port=5003 occupied=true exit=1 decision=OCCUPIED_PORT_REJECTED_PASS
DEMO_SEED_COMPLETE count=5
RJUDI_MVP_WAVE_2_V1 PASS
OBSERVATION gate=RJUDI_MVP_WAVE_2_V1 head=5d43de82a4a28d93c82e22e31bc979a8005aceef case_count=5 port=5002 cleanup=PASS decision=CONTROLLED_DEMO_EXECUTED_PASS
```

All eight jobs in the run executed and succeeded. Full-suite job 112453298823 executed 410 tests: 407 succeeded, zero failed and three real-corpus skips. The existing M1, D/E, local-MVP and J/K gates were retained and passed.

DOCUMENTED: frozen protocol, failed attempts, raw execution identities.
IMPLEMENTED: portable configured-port guard, truthful detached metadata, owned process-tree cleanup and native CI.
EXECUTED: both fixtures and the six canonical demo steps (migration, seed, API health, five CNJ lookups, five document observations, unknown CNJ 404).
ACCEPTED: controlled five-case Linux-hosted demo mechanics only. No empirical sample-size, real source, production authentication, Windows or model-quality acceptance.

Preserve the final successful JSON with native upload-artifact (30-day retention), print its SHA-256 and serialized observation in logs, and fail if it is missing. Assert exactly the six named steps, five lookup/document observations and 404, rather than accepting an empty set of successful steps. The final documentation/artifact-preservation commit requires its own checks before merge. Synthetic dataset content is the only case evidence; no keys or real legal corpus is uploaded.
