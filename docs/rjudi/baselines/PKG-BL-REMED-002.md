# PKG-BL-REMED-002

Purpose: restore the remaining baseline test suite after PKG-BL-REMED-001 restored compilation.

Execution evidence: GitHub Actions run `34078752845`.

Observed state:
- Restore: PASS
- Build: PASS
- Database migrator (run twice): PASS
- Tests: FAIL
- Total tests: 150
- Passed: 141
- Failed: 9

Failure groups:
1. OpenAI generation tests
   - valid-context fixture uses a stale citation length and is rejected by exact citation validation;
   - malformed inner JSON leaks `JsonReaderException` instead of the expected fail-closed `InvalidOperationException` contract.
2. OAB/RulingBR A/B runner tests
   - JSON property casing expectation does not match serialized record property names;
   - deterministic comparison includes volatile `Runtime` timestamp.
3. Benchmark CLI tests
   - hard-coded Windows corpus root `C:\\Projetos\\RJ\\oab-bench` is not portable to GitHub Actions/Linux.
4. OAB/RulingBR catalog adapter tests
   - tests depend on developer-local external corpora under `C:\\Projetos\\RJ` instead of hermetic fixtures.

Remediation requirements:
- keep exact citation validation unchanged;
- make test fixtures self-contained and cross-platform;
- normalize malformed provider/model output to the public fail-closed exception contract;
- preserve semantic determinism while excluding or controlling volatile timestamp metadata;
- do not introduce RJudi domain behavior changes in this package.

Out of scope:
- DOM-001 and later M1 domain packages;
- vector/hybrid/reranker work;
- provider selection;
- attachment ingestion/OCR.
