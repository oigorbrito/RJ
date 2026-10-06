# External evidence admission boundary execution

Source frozen at ad670f50c8925481f79385d01c980aeb9bc6199e. Repository contents provide a Judit fixture, but no admitted authentic DataJud response, frozen 30–50-case EVAL-010 corpus/oracle manifest, attachment binary/extracted-text manifest or paired treatment selection manifest. Do not relabel the existing Judit fixture as these missing inputs.

Run the existing F/G/H/I canonical scripts in four independent native CI jobs. Each executes its existing focused contract tests, then encounters either all absent inputs or one incomplete input. Acceptance of this negative experiment requires focused tests actually succeeding, exit 2 and the gate's exact BLOCKED marker. A generic exception, test failure or exit 1 must not be counted as a valid blocked classification.

Potential implementation problem: each script sets ErrorActionPreference=Stop then invokes Write-Error before exit 2 in its missing-input branch. Verify actual behavior before correcting it. Preserve any executed failure.

DOCUMENTED: missing-input protocol and real-input boundary.
IMPLEMENTED: native matrix, literal head, missing/partial temporary environment fixtures.
EXECUTED/ACCEPTED: pending actual logs; no PASS yet.

A successful guard job means missing evidence is rejected with a distinguishable blocked outcome. Real evidence remains NOT_EXECUTED and authentic-source/corpus/treatment acceptance remains NOT_PROVEN. No paid API, provider credentials, repository-rule writes, issue closure or branch deletion. Temporary environment values are removed in finally; hosted runners are disposable.

## Initial executed classification failures — 2026-10-06

Head 750182a5266135871002968acddd9118bdb47618; run 37518634706.

| Gate | Job | Focused contracts | Missing-input result |
| --- | --- | --- | --- |
| F / DataJud | 112458002240 | 11 passed, zero failed/skipped | BLOCKED SRC-003 printed; exit 1 rather than 2; guard FAIL |
| G / EVAL-010 | 112458002313 | 14 passed, zero failed/skipped | BLOCKED RJ-BLK-003 printed; exit 1 rather than 2; guard FAIL |
| H / attachments | 112458002274 | 11 passed, zero failed/skipped | BLOCKED ATT-001 printed; exit 1 rather than 2; guard FAIL |
| I / paired selection | 112458002135 | 20 passed, zero failed/skipped | BLOCKED RJ-BLK-003 printed; exit 1 rather than 2; guard FAIL |

All four failed before their partial-input experiment; no real verifier executed. This is an executed blocked-outcome classification defect, not a failed authentic-source or corpus experiment.

Correction: override ErrorAction to Continue only on each missing-evidence diagnostic, allowing the existing explicit exit 2 to execute. All contract/test/build failures, real-input verifiers and admission policy remain unchanged. Keep stderr diagnostics and finally cleanup. The native matrix now uses concise gate names; no semantic engine or fallback is added.
