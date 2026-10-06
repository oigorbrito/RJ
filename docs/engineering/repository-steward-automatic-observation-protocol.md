# Automatic native readiness observation

Prospective scope: after the existing ci workflow completes for a pull_request event, run the read-only observer from the default branch. No comment or rules setup required. Preserve the v2 classifier unchanged. Human merges remain distinct from observer authority.

Use GitHub-native Commit.associatedPullRequests to associate the triggering head SHA with exactly one OPEN PR whose current head matches. No matching PR, ambiguous matches, paginated/incomplete or invalid response -> READINESS_UNKNOWN; no candidate. Recheck exact head when querying readiness to fail closed on intervening pushes. Do not execute PR code, consume CI artifacts or add write permissions.

Acceptance: shell association tests (matching, stale head, closed, ambiguity, pagination and GraphQL error) and unchanged classifier matrix; hosted CI on exact implementation head; then actual workflow_run-triggered observation on a doc-only fixture, exact CI/run/job/head identities and observer absence from head check rollup. Fixture closes without merge. Automatic execution cannot be proven before activation on main; implementation CI does not promote native automation to PASS.

Policy/review fixtures #39-41 are optional for the user's operating profile and closed unmerged; native outcomes remain NOT_PROVEN. Current observer operates only READ/REPORT. Other projects or release/provider/corpus qualification is outside scope.
