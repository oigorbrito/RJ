# External evidence admission boundary execution

Source frozen at ad670f50c8925481f79385d01c980aeb9bc6199e. Repository contents provide a Judit fixture, but no admitted authentic DataJud response, frozen 30–50-case EVAL-010 corpus/oracle manifest, attachment binary/extracted-text manifest or paired treatment selection manifest. Do not relabel the existing Judit fixture as these missing inputs.

Run the existing F/G/H/I canonical scripts in four independent native CI jobs. Each executes its existing focused contract tests, then encounters either all absent inputs or one incomplete input. Acceptance of this negative experiment requires focused tests actually succeeding, exit 2 and the gate's exact BLOCKED marker. A generic exception, test failure or exit 1 must not be counted as a valid blocked classification.

Potential implementation problem: each script sets ErrorActionPreference=Stop then invokes Write-Error before exit 2 in its missing-input branch. Verify actual behavior before correcting it. Preserve any executed failure.

DOCUMENTED: missing-input protocol and real-input boundary.
IMPLEMENTED: native matrix, literal head, missing/partial temporary environment fixtures.
EXECUTED/ACCEPTED: pending actual logs; no PASS yet.

A successful guard job means missing evidence is rejected with a distinguishable blocked outcome. Real evidence remains NOT_EXECUTED and authentic-source/corpus/treatment acceptance remains NOT_PROVEN. No paid API, provider credentials, repository-rule writes, issue closure or branch deletion. Temporary environment values are removed in finally; hosted runners are disposable.
