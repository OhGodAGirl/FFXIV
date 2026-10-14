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

On Windows, install Git and the .NET SDK specified in `global.json`. Set `DALAMUD_HOME` to the directory containing the Dalamud API 15 development assemblies. To download, build, and test the source, run:

```powershell
git clone https://github.com/OhGodAGirl/FFXIV.git
cd FFXIV/Exportaramus
dotnet restore project/Exportaramus.slnx --locked-mode
dotnet build project/Exportaramus.slnx -c Release --no-restore -p:ShouldUnsetParentConfigurationAndPlatform=false
dotnet test tests/Exportaramus.Tests/Exportaramus.Tests.csproj -c Release --no-build --no-restore -- xUnit.MaxParallelThreads=2
```

The plugin package is written to `project/bin/Release/Exportaramus/latest.zip`.

### Directory Layout

- `project/` — Project, solution, dependency locks, manifest, and build configuration.
- `src/` — Plugin code, interface, and runtime contracts.
- `data/` — Collection data, schemas, and export mappings embedded in the plugin.
- `images/` — Plugin icon.
- `tests/` — Automated tests and test configuration.
- `tools/` — Build packaging and package validation scripts.

The README, license, SDK selection, Git exclusions, and editor settings live at the top level.

## License

[MIT](LICENSE)
