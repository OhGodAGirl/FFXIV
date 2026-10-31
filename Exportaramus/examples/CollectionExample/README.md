# Collection Example

A small Dalamud plugin that reads data from Exportaramus and prints it in FFXIV's chat. Dalamud is the framework that loads plugins inside the game. Exportaramus provides the recorded data; this example is the consumer that reads it.

| Command | Result |
| --- | --- |
| `/collectionexample` | The logged-in character's recorded mount count. Mounts are creatures or vehicles unlocked for travel. |
| `/collectionexample item <base-item-id>` | An item's last-observed normal/high-quality quantities and storage locations. Use the numeric ID of a row in the game's Item data table. |

Enter these commands in the game's ordinary chat input, not PowerShell. Each invocation prints a new message; earlier messages do not update.

## Requirements

- A working FFXIV installation with Dalamud. This project targets Dalamud API 15 through `Dalamud.NET.Sdk/15.0.0`; use matching installed host assemblies.
- A .NET 10 SDK. Inside the Exportaramus development repository, use the SDK selected by its `global.json`.
- The Exportaramus source tree, including `src/Contracts/1.2`, `data/Contracts/1.2`, and `project/ConsumerKit/1.2`. The checksum manifest in the last directory lists the kit files with paths relative to the repository root.
- Exportaramus installed and enabled for in-game queries. A local kit or plugin DLL does not make the provider run.

The kit supplies the CollectionData connection. `InventoryItemReader.cs` is a separate example parser for Inventory 1.0. The example does not reference the Exportaramus plugin DLL.

## Build

Open PowerShell in this directory. Set `DALAMUD_HOME` to the installed API 15 directory containing `Dalamud.dll`. For the reviewed host, that directory is:

```powershell
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncher\addon\Hooks\15.0.3.9"
dotnet build CollectionExample.csproj -c Release --artifacts-path ./artifacts -p:RestoreLockedMode=true
```

The build restores the dependency versions recorded in `packages.lock.json`. `--artifacts-path` keeps generated files in this example's `artifacts/` directory, and keeps generated files out of the kit source. The development tree may redirect kit outputs to its shared build directory.

The project finds Kit 1.2 in the Exportaramus source tree automatically. If the example lives in another repository, pass the Exportaramus source root:

```powershell
dotnet build CollectionExample.csproj -c Release --artifacts-path ./artifacts -p:RestoreLockedMode=true -p:ConsumerKitPath="../Exportaramus"
```

`ConsumerKitPath` names the Exportaramus source root. Preserve the relative paths of the kit source, project, and data files; copying only its DLL or the example files is insufficient to build this project.

Locate `CollectionExample.dll` under `artifacts/bin/CollectionExample/`. Its output directory must also contain `CollectionExample.json` and `Exportaramus.ConsumerKit.V1_2.dll`. Keep those files together when loading the example. A successful build does not prove the plugin loaded or a query succeeded.

## Load and Check

1. Enter `/xlsettings` in the game's chat. Under Experimental, add the full path of the built `CollectionExample.dll` to Dev Plugin Locations, then save.
2. Enter `/xlplugins`. Under Installed Dev Plugins, enable Collection Example and check for load errors. Keep its manifest and the Consumer Kit DLL beside it.
3. Log into a character and enter `/collectionexample` in chat.
4. Enter `/exportaramus`, select **Sync Now**, wait for capture and the example's next connection check, then run `/collectionexample` again. Earlier chat messages do not change.
5. Disable Exportaramus in `/xlplugins` while leaving the example enabled. After the next connection check, run `/collectionexample` again; it should report unavailable data. Re-enable Exportaramus and repeat to check recovery.

Also check logout, switching characters, and unloading the example with the exact build you intend to distribute.
Run `/collectionexample` while logged in. A count confirms that the example accepted a complete mount observation for that character. An unavailable message means it could not obtain an acceptable observation; it does not mean the character has zero mounts.

## Files and Limits

| File | Responsibility |
| --- | --- |
| `CollectionExample.csproj` | Plugin metadata, SDK target, and kit reference. |
| `Plugin.Dalamud.cs` | Command registration, character identity, connection updates, one inventory lookup at a time, and cleanup. |
| `InventoryItemReader.cs` | Check inventory replies before displaying their contents. |

The example displays recorded data. It does not move items, change unlocks, open storage, or request captures. Unknown storage is not empty; retained observations keep their original age.

