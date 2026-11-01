<p align="center">
  <img src="images/icon.png" width="128" alt="Local collection snapshots and sharing.">
</p>

# Exportaramus

Exportaramus keeps track of your FFXIV collections and stored items. Share your
snapshot with compatible plugins, or export your progress to
[FFXIV Collect](https://ffxivcollect.com/).

## Features

- Tracks collections including mounts, minions, orchestrion rolls,
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

## Plugin Integration

To use Exportaramus data in another plugin, see the
[Consumer Integration Guide](<docs/Consumer Integration Guide.md>),
[example plugin](examples/CollectionExample/README.md), and
[API reference](<docs/Exportaramus - Backend Specification.md#13-collectiondata-ipc>).

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
