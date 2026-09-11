# RJudi MVP — Wave 2 closure

Status: `PASS`

Gate: `RJUDI_MVP_WAVE_2_V1`

## Scope

This wave establishes the product-level multi-process persistence and lookup path used by the MVP demo:

- PostgreSQL schema version 5;
- persistent `process_cases` catalog;
- five project-scoped demo cases with distinct CNJs;
- persistent legal documents per case;
- lookup by CNJ and by case identifier;
- authorization of the five demo case identifiers in explicit demo mode;
- negative lookup returning HTTP 404.

The choice of five fixtures is a `PROJECT_DECISION`; it is not an empirical sample-size claim and must not be generalized beyond this demo scope.

## Executed gate

Canonical command:

```powershell
$env:RJ_POSTGRES_CONNECTION="Host=localhost;Port=5432;Database=rjudi;Username=rjudi;Password=rjudi_demo_2026"
.\scripts\test-rjudi-mvp-wave-2.ps1
```

Two consecutive local executions completed with `RJUDI_MVP_WAVE_2_V1 PASS` on 2026-09-11.

Observed local artifacts:

- `C:\Projetos\RJ\.artifacts\rjudi-mvp-wave-2\20260911-175029.json`
- `C:\Projetos\RJ\.artifacts\rjudi-mvp-wave-2\20260911-175102.json`

The raw artifacts are intentionally local and excluded from Git by `.artifacts/`; the paths above are evidence locators for the executed environment, not repository fixtures.

## Prior failed observations

The wave was not declared PASS on the first attempts. Earlier executions observed:

1. all CNJ lookups returning 404 and no documents, consistent with the wrong/old API instance being exercised;
2. CA1050 build failure in `RJ.DemoSeeder`, corrected by moving demo records into namespace-scoped types;
3. TCP port 5001 already occupied, causing the gate to fail closed until the conflicting listener was stopped.

These observations remain part of the wave history and are not retroactively reclassified as successful runs.

## Methodological classification

- preserving executed commands, commit/environment information and raw gate artifacts: `REPRODUCIBILITY_SUPPORTED`;
- requiring the end-to-end gate before declaring PASS: `DERIVED_FROM_METHOD`;
- PostgreSQL, endpoint naming, fixture count and demo authentication shape: `PROJECT_DECISION`.

Interpretation is limited to the executed local MVP fixture scope. This wave does not validate production data sources, production authentication, remote CI, or real generative-model quality.
