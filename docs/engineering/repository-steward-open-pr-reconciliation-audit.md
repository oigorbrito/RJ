# Open RJ PRs — read-only reconciliation audit

## Scope

Current main: bf0b761c058f6fa65c8535d7cfdce44377f0b0f5. Source review and native GitHub metadata only. No product branches were edited, reviews submitted, checks rerun or product PRs merged/closed. This is repository-steward reporting, not project closure; the closure template was not activated.

## Exact-head CI inventory

| PR | Current head | Target | Draft | Run | runner-smoke job | build-test job | Execution |
|---|---|---|---|---|---|---|---|
| #22 | 823b4b6039bbac438d996049b9bf030081859efe | main | false | 34249059374 | 102138481953 | 102138502826 | runner failure with steps=null; build skipped with steps=null |
| #23 | 5efd5f686b4c5812597d97dc86b470a80dfb7eca | rjudi/m1-consolidated | false | 34252011299 | 102148461249 | 102148481062 | runner failure with steps=null; build skipped with steps=null |
| #24 | 63e316b4eef7c0338390362b960386ea99030b65 | preserve/rjudi-local-2026-09-08-192438 | true | 34561861518 | 103146071990 | 103146080525 | runner failure with steps=null; build skipped with steps=null |
| #25 | 59b1e50b6399d8fe043074fb52fc2def648aa8d1 | rjudi/wave-c-operational-security-001 | true | 34563827129 | 103151761710 | 103151771295 | runner failure with steps=null; build skipped with steps=null |
| #26 | cde8819b5d7a845793d569e3cbbecf588c9c4e78 | rjudi/wave-d-postgres-persistence-001 | true | 34564550182 | 103153875204 | 103153881542 | runner failure with steps=null; build skipped with steps=null |
| #27 | ba85b3844af2fb26ad67e3e7234c192e71712e3d | rjudi/wave-e-refresh-retention-001 | true | 34566006558 | 103158154362 | 103158162912 | runner failure with steps=null; build skipped with steps=null |
| #28 | 30033c517c39dcf6ceb5a7a82d85d65acafdee64 | rjudi/wave-f-datajud-source-001 | true | 34567849125 | 103163522959 | 103163529191 | runner failure with steps=null; build skipped with steps=null |
| #29 | 23c26f7730baec6b05c9231af35406c0d7c69b90 | rjudi/wave-g-eval010-corpus-admission-001 | true | 34614677345 | 103313520121 | 103313535482 | runner failure with steps=null; build skipped with steps=null |
| #30 | 6ae960f5cd9ce375a1ec75b025a29cfe51250794 | rjudi/wave-h-attachment-admission-001 | true | 34616947761 | 103321123010 | 103321141684 | runner failure with steps=null; build skipped with steps=null |
| #31 | 90dfec9dee9984da480bcc1cf02b637bac084b8a | rjudi/wave-i-empirical-selection-001 | true | 34619696820 | 103330294334 | 103330306912 | runner failure with steps=null; build skipped with steps=null |
| #32 | 86d9b464eff9d76897d6d275bc96f5de4ce0810a | rjudi/wave-j-observation-materialization-001 | false | 34628730865 | 103363864228 | 103363877063 | runner failure with steps=null; build skipped with steps=null |
| #33 | e687c99e8a28ce48751df8f3090fd6de2c96aa0c | main | false | 34666096788 | 103478165044 | 103478169995 | runner failure with steps=null; build skipped with steps=null |

All listed check-associated runs failed before repository commands. This does not demonstrate product failure or PASS. Historical runner availability cannot qualify these heads; the now-working main CI is distinct evidence. No manual reruns were issued.

## Native commit lineage

Comparisons use exact PR head SHAs; GitHub compare status ahead with behind_by=0 proves ancestry, not acceptance or unchanged behavior.

| PR head compared with #33 | Status | #33 ahead by | #33 behind by |
|---|---|---|---|
| #22 | diverged | 269 | 1 |
| #23 | ahead | 261 | 0 |
| #24 | ahead | 234 | 0 |
| #25 | ahead | 208 | 0 |
| #26 | ahead | 185 | 0 |
| #27 | ahead | 173 | 0 |
| #28 | ahead | 153 | 0 |
| #29 | ahead | 138 | 0 |
| #30 | ahead | 98 | 0 |
| #31 | ahead | 73 | 0 |
| #32 | ahead | 39 | 0 |

#23-32 are ancestors of #33. They form a stacked/cumulative product line, not ten independent candidates to merge in parallel. Keep historical PRs until an integration decision; ancestry alone is not grounds to discard changes or claim PASS.

#22 diverges by one unique commit, 823b4b6039bbac438d996049b9bf030081859efe (fix: resolve DataJud DI and test gate regression in M1). Its delta removes unsupported DataJudProcessSourceAdapter DI registration and DataJudProcessSourceAdapterTests entries from the M1 script/manifest. In #33, direct file inspection confirms that registration and those test-name entries are already absent. This establishes presence of the specific correction by source inspection, not complete product equivalence or executed gate acceptance.

## Integration candidate and boundaries

#33 head e687c99e8a28ce48751df8f3090fd6de2c96aa0c is the broad consolidated MVP candidate. All 191 changed files were enumerated across both REST pages. This audit does not claim a complete semantic review of all 191 files. Relative to main it has 277 commits ahead, 24 behind, common ancestor c627a1bcdc87ff9b0bbd5ccc0b7d108daa5e324d. #22 is 9 ahead and 24 behind the same main. Mergeability is not readiness.

Source findings requiring reconciliation if the MVP is integrated:

- Preserve main's qualified observer workflows and CI repair. The old branch has different application/tests and cannot inherit main's test PASS.
- #33 OabRulingBrAbRunner emits DateTimeOffset.UnixEpoch while main's accepted repair retains an actual timestamp and handles reproducibility in tests. This is a material timestamp-contract difference; do not silently overwrite it.
- #33 has DemoAuthenticationMiddleware, wired in Program.cs, that assigns fixed demo identity/case claims when RJUDI_DEMO_MODE=true. It is explicitly a demo mechanism, not production authentication acceptance.
- #33 documentation includes local MVP PASS claims with raw artifacts at Windows .artifacts paths excluded from Git. These are historical claims/locators, not verified exact-head hosted acceptance in this audit.
- Authentic DataJud, authorized attachment/extraction and frozen real-corpus/oracle/treatment evidence remain external boundaries according to the branch blocker register. They need not block hermetic integration tests, but cannot be converted into real-data/provider/production PASS.

## Outcome

STEWARDSHIP_TRIAGE=EXECUTED_READ_REPORT. PRODUCT_HEAD_ACCEPTANCE=NOT_PROVEN for #22-33 in the inspected hosted evidence. Do not request artificial reviews or install rules to make these PRs look ready. The concrete next scope is reconciling the consolidated MVP #33 with current main on an isolated integration branch, preserving qualified main behavior and obtaining actual CI execution. That is product integration, beyond the completed observer qualification. No product merge or close decision was executed by this audit.
