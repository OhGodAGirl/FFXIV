<p align="center">
  <img src="images/icon.png" width="128" alt="Local collection snapshots and sharing.">
</p>

# Exportaramus

Exportaramus keeps track of your FFXIV collections and stored items. Share your
snapshot with compatible plugins, or export your progress to
[FFXIV Collect](https://ffxivcollect.com/).

## Features

- Tracks 25 collection types, including mounts, minions, orchestrion rolls,
  Triple Triad cards, achievements, quests, outfits, relics, and Beasts.
- Keeps your collections up to date as you play.
- Captures your Glamour Dresser, Armoire, and retainers with guided refreshes.
- Lets compatible plugins see which items you have and where they are stored.
- Copies your collection progress for import into FFXIV Collect.
- Adapts its appearance to your selected FFXIV theme.

## Installation

You need XIVLauncher with Dalamud enabled. If you already added our repository,
skip to step 4.

1. Open Dalamud Settings with `/xlsettings` in game.
2. Under **Experimental → Custom Plugin Repositories**, paste this URL:

   ```text
   https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/pluginmaster.json
   ```

3. Click **+**, then **Save**.
4. Open the Plugin Installer with `/xlplugins`, search for **Exportaramus**,
   and select **Install**.

## Usage

1. Run `/exportaramus` in game.
2. Select **Sync Now** to refresh your collections.
3. To include stored items, use the guided options:
   - **Dresser & Armoire** — move within interaction range of your Glamour Dresser and Armoire when prompted; Exportaramus opens each one and captures its contents.
   - **Retainers** — stand near a Summoning Bell to capture your retainers.
4. Check **Snapshot Health**. Hover over its status for details if something
   could not be read.

Guided capture opens the required game windows but does not move your character.
You can cancel it at any time; completed captures are saved. Retainer progress
appears as captured/total, such as **4/4**.

### Export To FFXIV Collect

1. Sign in to [FFXIV Collect](https://ffxivcollect.com/) and select your character.
2. If the account menu shows **Verify Character**, select it and follow the
   website's ownership-verification instructions. **Import Progress** requires
   a verified character.
3. In Exportaramus, refresh your collections, then select **Copy JSON**.
4. Open **Import Progress** from the account menu, or go to the
   [Import page](https://ffxivcollect.com/import). Paste the copied JSON and
   select **Verify**.
5. Check that the selected character is correct, review the collection counts,
   then select **Import** to apply the changes.

Review carefully: importing replaces the included collections on the website.
Newly added collectibles may not be included until Exportaramus is updated.

## Commands

`/exportaramus` — Open Exportaramus.

`/exportaramus diagnostics` — Open diagnostics for troubleshooting.

## For Developers

### Building From Source

On Windows, install Git and the .NET SDK specified in `global.json`. Use PowerShell 7.5 or newer. Set `DALAMUD_HOME` to an existing Dalamud API 15 development directory, or run the dependency download shown below. It downloads a pinned, checksum-verified API 15 archive without installing or launching the game. To download, build, and test the source, run:

```powershell
git clone https://github.com/OhGodAGirl/FFXIV.git
cd FFXIV/Exportaramus
./tools/Get-BuildDependencies.ps1
dotnet restore project/Exportaramus.slnx --locked-mode
dotnet build project/Exportaramus.slnx -c Release --no-restore -p:ShouldUnsetParentConfigurationAndPlatform=false
dotnet test tests/Exportaramus.Tests/Exportaramus.Tests.csproj -c Release --no-build --no-restore -- xUnit.MaxParallelThreads=2
```

The plugin package is written to `project/bin/Release/Exportaramus/latest.zip`.

The download sets `DALAMUD_HOME` for the current PowerShell session. On later sessions, set `$env:DALAMUD_HOME = (Resolve-Path .tools/dalamud/15.0.3.9).Path` instead of downloading again. You can pass `-Destination` to select another new directory.

GitHub Actions runs the offline checks on Windows for Exportaramus pushes and pull requests. Results and the build package are retained for seven days; a passing run does not publish a release or establish in-game acceptance.

### Building the Consumer Kit

The Consumer Kit lets another plugin read Exportaramus data without referencing the Exportaramus plugin DLL. Its source and schemas share this repository's existing folders.

```powershell
dotnet restore project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj --locked-mode
dotnet build project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj -c Release --no-restore
dotnet test tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj -c Release -p:RestoreLockedMode=true
```

The [Collection Example](examples/CollectionExample/README.md) shows how to build and load a consumer plugin. Use the full project above for consumers; the projects under `project/Contracts/` build the provider's smaller runtime libraries. Do not reference both variants in the same plugin.

The full API reference is in the [Backend Specification](<docs/Exportaramus - Backend Specification.md#13-collectiondata-ipc>).

The [Consumer Integration Guide](<docs/Consumer Integration Guide.md>) walks through the example and explains how to interpret collection and inventory observations. The [Frontend Specification](<docs/Exportaramus - Frontend Specification.md>) documents the interface and its acceptance cases.

### Verification

Use PowerShell 7.5 or newer and Python 3.11 or newer, alongside the .NET SDK and Dalamud development assemblies described above. Install the pinned Python verification dependencies into a virtual environment:

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -r tools/contract-python-requirements.txt
pwsh -File tests/run-quality-gates.ps1 -Python .venv/Scripts/python.exe
```

The scripts resolve `dotnet` and `python` from PATH by default. Pass `-Dotnet` and `-Python` to select executable paths, and `-OutputDirectory` to choose where verification results are written. Generated results stay separate from source inputs.

The portable checks cover locked restore, compilation, package inspection, provider and ConsumerKit behavior, strict coverage, canonical schemas and fixtures, checksums, and the source scope. The full API reference remains in the Backend Specification. Mutation qualification and supervised in-game acceptance are separate from these offline checks; a passing portable run does not establish either.

The [mutation policy](tests/mutation-policy.json) requires a score of 100 and complete instrumentation evidence. The mutation runner covers the provider, separately referenced 1.1 contracts, the canonical 1.2 ConsumerKit, and the example inventory reader. [Performance budgets](tests/performance-budget.json) distinguish offline measurements from performance that requires a supervised game session.

### Mutation Testing In GitHub Actions

Open **Actions → Exportaramus Mutation Testing → Run workflow** in this repository. Choose the branch and a mode:

- **full** (default): run the ordinary offline checks, then all four mutation targets with Stryker 4.16.0 and two workers. The mutation step is limited to five hours; the job has an additional thirty minutes for setup and saving results.
- **setup-only**: verify dependencies, source scope, Stryker startup, and all three distinct baseline test suites without executing mutants. A successful setup-only run is not a passing mutation campaign.

Runs are manual; pushes and pull requests continue to use the ordinary verification workflow. One mutation run per branch executes at a time. Reports, logs, source hashes, and baseline results are uploaded as an artifact for seven days, including when the campaign fails. A timeout, incomplete instrumentation, or unresolved mutation result fails the campaign; the workflow does not waive survivors or change the required score. Timed-out runs may have only partial reports. Download evidence needed for a review before the artifact expires.

The workflow installs the SDK from `global.json`, .NET 8 for Stryker, the pinned Python dependencies, and the checksum-verified Dalamud build dependencies. It does not publish or install a plugin.

To run locally after completing the verification setup above:

```powershell
dotnet tool install dotnet-stryker --version 4.16.0 --tool-path .tools/stryker
$runner = @(Get-ChildItem .tools/stryker -Recurse -Force -Filter Stryker.CLI.dll)
if ($runner.Count -ne 1) { throw 'Expected one pinned Stryker runner.' }
./tests/test-mutation.ps1 -Runner $runner[0].FullName -Python .venv/Scripts/python.exe
```

Install .NET 8 alongside the project SDK if required by Stryker. Add `-SetupOnly` to verify the setup without running mutants. Results default to an ignored, unique directory under `development/mutation`; pass `-RunDirectory` to choose another new directory.

To check package reproduction with the same source, SDK, configuration, and dependency bytes:

```powershell
pwsh -File tools/Verify-Reproducibility.ps1
```

This rebuilds and compares the package and every runtime file. It verifies reproduction in the current checkout and environment; it does not claim identical output across different machines or checkout paths.

### Directory Layout

- `project/` — Project, solution, dependency locks, manifest, and build configuration.
- `src/` — Plugin code, interface, shared contracts, and consumer connection code.
- `data/` — Collection data, schemas, and export mappings embedded in the plugin.
- `images/` — Plugin icon.
- `tests/` — Automated tests, contract fixtures, and test configuration.
- `examples/` — Example consumer plugin with build and loading instructions.
- `docs/` — Integration guide, Backend and Frontend Specifications, and referenced images.
- `tools/` — Packaging, validation, source inventory, and reproducibility scripts.

The README, license, SDK selection, Git exclusions, and editor settings live at the top level.

## License

[MIT](LICENSE)

### Repository Maintenance Checks

Workflow Validation checks workflow syntax and expressions whenever Actions configuration changes. Exportaramus Publication Hygiene rejects tracked generated files, live snapshots, machine-specific paths, and private vault links. Deliberately invalid path fixtures retain explicit exceptions. This is not a general secret scanner. The ordinary quality gate continues to inspect the exact runtime package contents.

Exportaramus Dependency Security audits NuGet dependencies (including transitive dependencies) and the pinned Python requirements on changes, weekly, and manually. Advisory lookup or restore failures fail the check. Reports are retained for seven days. These checks cover known package advisories, not downloaded Dalamud binaries.

Dependabot proposes weekly grouped Actions, NuGet, and Python updates. Updates require review; no automatic merging is configured. The pinned Dalamud SDK is excluded from routine version updates and must be reviewed separately for API compatibility.
