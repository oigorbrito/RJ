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
