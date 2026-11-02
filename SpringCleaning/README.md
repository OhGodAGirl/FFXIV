<p align="center">
  <img src="images/icon.png" width="128" alt="Spring Cleaning folded-outfit icon">
</p>

# Spring Cleaning

Spring Cleaning helps you organize your FFXIV glamour items. Store eligible gear
in the Glamour Dresser or Armoire, and remove spare copies of appearances you
already have stored.

## Features

- Stores eligible outfit pieces from Inventory or the Armoury Chest in the Glamour Dresser.
- Stores eligible Inventory items in the Armoire and moves eligible Dresser entries there.
- Finds duplicate carried items whose appearance is already in the Dresser or Armoire.
- Removes redundant Dresser entries by restoring them to Inventory before discard.
- Excludes gear-set items from Dresser storage and duplicate discard, with separate options to protect materia and fully spiritbound items from discard.
- Adds controls beside the game's Glamour Dresser and Armoire windows.

## Installation

You need XIVLauncher with Dalamud enabled and
[Exportaramus](../Exportaramus/README.md) installed. Spring Cleaning uses
Exportaramus for collection and inventory data; keep it enabled. If you already
added our repository, skip to step 4.

1. Open Dalamud Settings with `/xlsettings` in game.
2. Under **Experimental → Custom Plugin Repositories**, paste this URL:

   ```text
   https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/pluginmaster.json
   ```

3. Click **+**, then **Save**.
4. Open the Plugin Installer with `/xlplugins`, install **Exportaramus** if
   needed, then search for **Spring Cleaning** and select **Install**.

**vnavmesh** is optional. It enables movement between the Dresser and Armoire;
without it, each furnishing must already be within interaction range when needed.

## Usage

1. Open your Glamour Dresser or Armoire. Spring Cleaning's tabs appear beside it.
2. Hover over a tab to see its action. Select a storage tab to start:
   - **Inventory to Glamour Dresser** — store eligible outfit pieces from Inventory.
   - **Armoury Chest to Glamour Dresser** — store eligible outfit pieces directly from the Armoury Chest.
   - **Glamour Dresser to Armoire** — move eligible Dresser entries into the Armoire.
   - **Inventory to Armoire** — store eligible Inventory items from the Armoire window.
3. Leave the relevant game windows and furnishings available while items are stored.

Hover over an unavailable action to see what it needs. If Exportaramus data is
missing or out of date, open the relevant storage windows to refresh it. Spring
Cleaning waits for the required data and rechecks each action against the game.
Storage follows FFXIV's normal rules, including any loss of item customization.

### Duplicates

1. Select **Duplicates** beside the Glamour Dresser.
2. Review the item names and locations. Use an item's trash control to discard
   that copy, or select **Discard All Duplicates** to process eligible copies.
3. Dresser entries are restored to Inventory before discard, so leave free
   Inventory slots. Use the **X** beside cleanup progress to cancel.

To keep an item, right-click its icon, name, or location and select **Ignore
Duplicate**. Enable **Show Ignored**, then right-click the item and select
**Stop Ignoring Duplicate** to include it again.

Discarding is irreversible. Spring Cleaning automatically confirms its own
matching FFXIV discard prompts. Gear-set items are excluded; materia and 100%
Spiritbond protections are enabled by default. Dyes and other customization do
not prevent a duplicate from being discarded.

### Settings

Open **Settings** beside the Dresser or Armoire to choose a **Tab Style**,
enable or disable **Store Partial Outfits**, adjust **Protect Items with Materia**
and **Protect Items with Spiritbond**, or enable **Remember Window Placement**.
The materia and Spiritbond options apply only to discarding duplicates.

## Commands

`/springcleaning diagnostics` — Open diagnostics for troubleshooting. Use
**Copy Diagnostics** when reporting a problem at the
[issue tracker](https://github.com/OhGodAGirl/FFXIV/issues).

The main controls appear beside the game's storage windows; `/springcleaning`
on its own does not open a window.

## License

[MIT](LICENSE)
