# Contributing to Collection Shopper

Discuss a feature before implementation when it changes purchasing safety,
native Market Board access, data sent to an external service, or the sidecar UI.

## Changes

- Keep changes within Collection Shopper's documented purpose.
- Preserve one-request-at-a-time purchasing and observed game confirmation.
- Minimize unrelated formatting, renaming, and cleanup.
- Add or update tests for policy and confirmed regressions.
- Update player documentation when behavior, setup, privacy, or limitations change.
- Do not commit packages, staged plugins, personal data, machine paths, or local
  development-governance material.

## AI assistance

Disclose AI assistance when required by the destination repository or
distribution policy. Contributors remain responsible for reviewing and
explaining every submitted line and for owning the validation evidence.

## Validation

From the Collection Shopper source folder, run:

```powershell
dotnet restore .\CollectionShopper.slnx --locked-mode
dotnet format .\CollectionShopper.slnx --no-restore --verify-no-changes
dotnet build .\CollectionShopper.slnx -c Release --no-restore
dotnet test .\CollectionShopper.slnx -c Release --no-build --no-restore
```

For locked restore, Release build, tests, per-file coverage, and package checks, run
`pwsh ./tools/Test-Repository.ps1`. Set `DALAMUD_HOME` to the API 15 development
assemblies before building. Checks write only local build and test artifacts.

Inspect the generated ZIP and test an explicitly installed candidate in game. A successful
build is not evidence that a Market Board action worked correctly.
