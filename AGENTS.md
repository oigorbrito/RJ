# Agent Instructions

## Project closure checklist activation

The reusable closure template is `.project/closure/PROJECT-CLOSURE-DOCUMENTATION-TEMPLATE.md`.

Do not instantiate, execute, or populate it merely because it exists. Activate it only when the user explicitly requests project closure, readiness/release closure, a final checklist, a closure audit, or an equivalent assessment.

Before using it, the responsible agent must:

1. read the template and the repository's current authoritative documentation;
2. classify the template as `VALID_AS_IS`, `NEEDS_ADAPTATION`, or `NOT_APPLICABLE` for the requested scope;
3. identify concrete project facts that justify adding, removing, or changing criteria;
4. present those adaptations explicitly and never silently rewrite criteria to fit a desired outcome;
5. instantiate a working project-specific checklist only after scope and applicability are established;
6. not run tests, create/close issues, merge PRs, or perform implementation work solely because the template exists.

Maintain these distinctions:

```text
DOCUMENTED != IMPLEMENTED
IMPLEMENTED != EXECUTED
EXECUTED != ACCEPTED
ISSUE_CLOSED != PROJECT_CLOSED
PR_MERGED != PROJECT_CLOSED
BLOCKED != PASS
NOT_EXECUTED != PASS
```

Issues and PRs provide traceability, not closure evidence by themselves.