# Testing

Automated checks cover managed behavior. Native FFXIV interaction, rendering and
live IPC require separate in-game acceptance of the exact package.

With the pinned .NET SDK and Dalamud build dependencies available:

```powershell
./tests/test-repository.ps1
./tests/test-mutation.ps1
```

The repository check performs locked restore, formatting verification, a Release
build, ordinary tests, configured line/branch coverage and package smoke checks.
Ordinary tests include the performance budgets. Coverage excludes timing tests
because instrumentation changes their measurements.

`coverage-manifest.json` lists each plugin source file and every narrow exclusion.
The gate requires 100% line and branch coverage per included file. Pinned vendor
source is verified against the original checksum inventory and exercised through
consumer fixtures; it is not claimed as plugin-owned mutation coverage.

Mutation checks require the instrumentation probe to fail as expected, then
verify the decision-source inventory and reject behavior-changing survivors,
uncovered mutants and timeouts. Individually reviewed equivalent mutants remain
listed with their source-bound rationale; the raw score is retained.

`test-rigor.json` retains regression, generated-input and lifecycle inventories.
Historical measurement records are labeled as such and do not certify a new
checkout. Fresh test results are written to `tests/TestResults` and are not source
files. The icon artwork criterion remains deferred for the review candidate.
