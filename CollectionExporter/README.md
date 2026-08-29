<p align="center">
  <img src="images/icon.png" width="128" alt="CollectionExporter icon">
</p>

# CollectionExporter

A Dalamud plugin that exports your character's collection progress for
[FFXIV Collect](https://ffxivcollect.com/).

## Features

- Exports all collection types supported by FFXIV Collect's importer.
- Won't overwrite collections it can't read.
- Adds Outfits and Relics found in game without removing ones already saved to
  your public profile.

## Installation

1. Open Dalamud Settings with `/xlsettings`.
2. Select **Experimental**, then find **Custom Plugin Repositories**.
3. Add this URL and save:

   `https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/pluginmaster.json`

4. Open the Plugin Installer with `/xlplugins`.
5. Search for **CollectionExporter** and select **Install**.

## Basic export

1. Run `/collectionexporter` in game.
2. Click **Generate**, then **Copy JSON**.
3. Paste the JSON into [FFXIV Collect's Import page](https://ffxivcollect.com/import).
4. Review the changes, then submit the import.

## Optional FFXIV Collect profile

A profile is only needed for Outfits and Relics. Everything else works without
one.

1. Go to [FFXIV Collect](https://ffxivcollect.com/), sign in, and add or search
   for your character.
2. Make sure the character profile is public.
3. Open the character's profile and copy its URL. It will look like
   `https://ffxivcollect.com/characters/12345678`.
4. Paste the URL into CollectionExporter and select **Save Profile**.

If you skip this, Outfits and Relics are left out of the JSON and are not changed
on FFXIV Collect.

## Armoire and Glamour Dresser

FFXIV only makes these inventories available while you are using them. Import
them in two passes:

1. Open the **Armoire**. While it is open, generate, copy, and submit an import.
2. Open the **Glamour Dresser**. While it is open, repeat the export and import.

Keep the plugin loaded between passes. It remembers Outfit and Relic matches from
the first pass and includes them in the second.

## Supported collections

- Armoires
- Bardings
- Blue Magic
- Completed Leves
- Fashion Accessories
- Field Records
- Hairstyles
- Occult Records
- Orchestrion Rolls
- Outfits
- Portrait Frames
- Relics
- Triple Triad Cards
- Triple Triad NPC Victories
- Variant Dungeon Survey Records

## Build

### Requirements

- Windows
- XIVLauncher and Dalamud, launched at least once
- The .NET SDK version specified in `global.json`

If Dalamud is installed somewhere other than its default location, set
`DALAMUD_HOME` to its development assembly directory.

### Commands

```powershell
git clone https://github.com/OhGodAGirl/FFXIV.git
cd .\FFXIV\CollectionExporter
dotnet restore .\CollectionExporter.slnx --locked-mode
dotnet build .\CollectionExporter.slnx -c Release
dotnet test .\CollectionExporter.slnx -c Release --no-build
```

The installable package is created at
`bin\Release\CollectionExporter\latest.zip`.

## License

[MIT](LICENSE) © 2026 OhGodAGirl
