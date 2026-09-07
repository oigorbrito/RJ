# Empirical harness policy

This policy constrains recommendations made by the engineering harness.

## Methodological authority

A suggestion may be presented as methodological guidance only when it is supported by one of the following:

1. an empirical software engineering standard or method-specific checklist;
2. a reproducibility or artifact-evaluation criterion;
3. a methodological source explicitly identified by those standards as supporting guidance.

Primary references:

- ACM SIGSOFT Empirical Standards for Software Engineering: https://www2.sigsoft.org/EmpiricalStandards/
- ACM artifact-evaluation guidance as applied by ACM SIGs, including functional, reusable, available, and reproduced artifact criteria.
- Wohlin et al., *Experimentation in Software Engineering*, for systematic experiment planning, execution, analysis, validity, replication, and reporting.
- Shull, Singer, and Sjøberg (eds.), *Guide to Advanced Empirical Software Engineering*, for empirical-method selection, reporting, evidence synthesis, and replication.

## Required suggestion classification

Every non-trivial recommendation must be classified as exactly one of:

- `EMPIRICALLY_SUPPORTED`: directly supported by an applicable empirical software engineering standard or methodological guide.
- `REPRODUCIBILITY_SUPPORTED`: directly supported by a reproducibility or artifact-evaluation criterion.
- `DERIVED_FROM_METHOD`: a technical consequence required to satisfy an explicit empirical or reproducibility criterion.
- `PROJECT_DECISION`: an architectural or implementation choice not prescribed by the methodology.
- `HYPOTHESIS`: a proposal not yet demonstrated.
- `EMPIRICAL_DECISION_PENDING`: a choice that must be resolved by a pre-specified empirical comparison.
- `NOT_SUPPORTED`: no adequate methodological support was identified.

Only `EMPIRICALLY_SUPPORTED`, `REPRODUCIBILITY_SUPPORTED`, and `DERIVED_FROM_METHOD` may be described as methodological recommendations.

`PROJECT_DECISION`, `HYPOTHESIS`, and `EMPIRICAL_DECISION_PENDING` must be identified as such. `NOT_SUPPORTED` must not be recommended.

These classifications are not validation states. Validation states remain `PASS`, `FAIL`, `BLOCKED`, `NOT_TESTED`, and `NOT_APPLICABLE`.

## Prohibited grounds for recommendation

Do not recommend a technique merely because it is popular, modern, widely adopted, associated with a prominent vendor, or has many stars, forks, downloads, citations, or community mentions.

Do not introduce arbitrary thresholds, weights, composite scores, sample sizes, repetition counts, success criteria, exclusion rules, or post-hoc metrics and then present them as methodologically justified.

## Empirical comparisons

When the specification permits multiple implementation alternatives, the harness must not select one by preference. Before observing treatment results, define the applicable research question or evaluation objective, treatments, baseline, experimental unit, corpus or dataset, inclusion/exclusion rules, metrics, measurement procedure, configuration, seeds when relevant, decision rule, and validity threats.

Observed results must not retroactively redefine the protocol. If the pre-specified evidence does not distinguish the alternatives adequately, report `NO_CLEAR_WINNER` rather than manufacturing a ranking.

## Validity and scope

Interpret evidence only within the population, cases, environments, treatments, and measurements actually studied. Consider the validity and quality criteria applicable to the selected method, including conclusion validity, construct validity, internal validity, reliability, objectivity, and reproducibility where applicable.

Evidence from one fixture, one process, one court, one environment, or one configuration must not be generalized beyond that scope without additional evidence.

## Reproducibility and artifacts

For computational evidence, preserve the execution information needed to understand or rerun the study when applicable: exact commit, worktree state, runtime/SDK, relevant dependency versions, operating environment, dataset/query/schema versions and hashes, seeds, configuration, commands, test or observation counts, duration, exit codes, raw outputs/logs, protocol deviations, and observed side effects.

Artifacts used to support empirical claims should be documented, consistent with the claimed result, sufficiently complete for the stated evaluation, and exercisable when the environment permits. External dependencies or unavailable data must be recorded explicitly rather than silently treated as success.

## Architecture boundary

Empirical methodology governs how evidence is designed, collected, interpreted, and reproduced. It does not by itself select a programming language, framework, database, provider, library, class structure, file name, or architectural abstraction.

Such choices are `PROJECT_DECISION` unless an explicit requirement fixes them or they are a necessary consequence of an empirical/reproducibility criterion.

## Final harness rule

Before presenting an action as a methodological recommendation, the harness must be able to identify the empirical or reproducibility criterion that supports it. If it cannot, the action must be classified as a project decision, hypothesis, empirical decision pending, or not supported.