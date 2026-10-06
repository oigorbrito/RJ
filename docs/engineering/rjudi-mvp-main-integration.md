# RJudi MVP integration with qualified main

## Frozen inputs and scope

Main parent: cf793c0dc93f33555f03970fc40c6f9fe3f811ad. MVP source: PR #33 at e687c99e8a28ce48751df8f3090fd6de2c96aa0c. Common ancestor: c627a1bcdc87ff9b0bbd5ccc0b7d108daa5e324d. Integration uses a real two-parent merge commit on a separate branch; original product branches are preserved.

Scope: integrate the consolidated MVP code for engineering/hermetic CI qualification. No project-closure checklist activation, production release, real provider request, legal-data admission or production authentication acceptance.

## Reconciliation decisions

Three-way tree reconciliation preserves current main's observer, read-only permissions, automatic ci-completion trigger and application CI repair. Five paths changed on both sides: README, OpenAiGenerationModel, OabRulingBrAbRunner, OpenAiGenerationModelTests and RulingBrDemoCatalogAdapterTests.

- Keep actual DateTimeOffset.UtcNow report timestamp and camelCase JSON contract; do not replace runtime evidence with UnixEpoch.
- Keep main's user-payload-only oracle-isolation assertions and negative citation-length regressions, plus incoming MVP tests.
- Preserve explicit opt-in real-corpus tests and hermetic fixtures. Skipped real-corpus tests are NOT_EXECUTED, never real-data PASS.
- Keep current README and add a bounded MVP description. Demo authentication remains explicitly opt-in through RJUDI_DEMO_MODE=true and is not production authentication.
- Preserve malformed-JSON rejection behavior and incoming parser improvements.

## Prospective acceptance

Hosted exact integration-head CI must execute restore/build, PostgreSQL initialization and migrator twice, and full solution tests. Test failures after execution are FAIL and must be corrected; runner failure before commands is infrastructure BLOCKED. Local .NET execution is NOT_EXECUTED because no SDK is available. Shell/tree reconciliation is not application PASS.

Automatic observer association/decision must be inspected for the current integration head after CI completion. Hosted application PASS qualifies the integrated hermetic engineering scope, not historical exact heads #22-33 or Windows canonical scripts, real corpus, live provider, demo-to-production promotion, or project/release closure. Append exact head/run/job and counts after execution.

Historical source includes local PASS claims with raw artifacts outside Git; these are preserved as history, not promoted to hosted/exact-head acceptance. DataJud, real attachment/oracle corpus and paired provider selection remain NOT_PROVEN without their external evidence.

## Executed initial integration attempt

Head 0477dad94d8fe7a2ab73a6c57027c99c4ae4b0dc, CI run 37506132069; runner-smoke job 112415141190 executed PASS; build-test job 112415170840 executed restore, build and migration twice, then test FAIL. 409 total: 399 succeeded, 7 failed, 3 real-corpus tests skipped. Six HTTP failures and one architecture failure; not an infrastructure blocker.

Corrections: remove direct API-to-Domain reference using the existing authorized-case string contract; deny unauthorized summary evidence with explicit 403 without requiring an unconfigured authentication scheme; test-only identity now reads chunked JSON, admits the exact fixture crawler source and configures the size-contract factory. Adds a real HTTP regression for denied evidence returning 403 without a scheme. Production authorization is not loosened.

Local SDK 10.0.401 and runtimes 10.0.12 installed and --info executed. Local solution restore currently fails before compilation with no diagnostic errors; local build/test PASS is not claimed. Hosted revalidation on corrected exact head is required.
