<p align="center">
  <img src="images/icon.png" width="128" alt="Fast(er) Expert Deliveries">
</p>

# EZ Delivery

EZ Delivery makes Grand Company turn-ins and rewards faster. Hand in eligible
gear with one click, or find weapons, armor, barding, and furnishings missing
from your Exportaramus collection snapshot.

## Features

- Repeats Expert Deliveries using your selected in-game filter and order.
- Stops before a delivery would exceed your seal cap, including reward bonuses.
- Shows missing Quartermaster items in Weapons, Armor, Barding, and Furniture tabs.
- Buys one item, or one copy of each item in the current filtered list.
- Lets you stop a run without starting another transaction.
- Offers five tab styles and follows your selected FFXIV theme.

## Installation

This is a qualification build awaiting live validation. EZ Delivery is not yet available through the public
Plugin Installer. These instructions apply once it is published.

You need XIVLauncher with Dalamud API 15 and a supported game build. If you
already added our repository, skip to step 4.

1. Open Dalamud Settings with `/xlsettings` in game.
2. Under **Experimental → Custom Plugin Repositories**, paste this URL:

   ```text
   https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/pluginmaster.json
   ```

3. Click **+**, then **Save**.
4. Open the Plugin Installer with `/xlplugins`, search for **EZ Delivery**,
   and select **Install**.
5. Install [Exportaramus](https://github.com/OhGodAGirl/FFXIV/tree/main/Exportaramus)
   from the same repository to use missing-item shopping.

Expert Delivery works without Exportaramus.

## Usage

### Expert Delivery

1. Open **Expert Delivery** at your Grand Company Personnel Officer.
2. Choose the in-game item filter.
3. Click the Parcel & Arrow tab beside the game window to start.
4. Click it again to stop new hand-ins.

EZ Delivery uses the items allowed by your selected filter. It does not add
protection for HQ items, materia, spiritbond, or items bought for your collection.
Check the filter before starting.

### Quartermaster Shopping

1. Refresh your collections and stored items in Exportaramus.
2. Open **Seal Exchange** at your Grand Company Quartermaster.
3. Choose **Weapons**, **Armor**, **Barding**, or **Furniture** beside the game window.
4. Search the list if needed, then select **Buy** or **Buy All**.
5. Select **Stop** to stop new purchases.

Buy All uses the displayed list when you click it. It stops at the next item you
cannot afford; changing the search afterward does not change that run. If the
total exceeds your balance, the warning does not disable Buy All.

Shopping can use the last complete Exportaramus snapshot, including a saved
snapshot. Storage may have changed since capture. Incomplete data cannot establish
that an item is missing. Furniture checks storage only; placed furnishings are
not included.

### Troubleshooting

Run `/ezdelivery diagnostics` and select **Copy prepared report**. Include the
report, the action you clicked, what you expected, and what happened when reporting
a problem. The report identifies the loaded build and the latest operation checks.

Report problems in the [issue tracker](https://github.com/OhGodAGirl/FFXIV/issues).

## Commands

`/ezdelivery settings` — Open the EZ Delivery settings window.

`/ezdelivery diagnostics` — Open the EZ Delivery diagnostics window.

## For Developers

Build with the SDK pinned in `global.json` and the Dalamud API 15 development
assemblies. Set `DALAMUD_HOME`, then run `./tools/Build.ps1` and
`./tools/Test.ps1 -IncludeHost` in PowerShell 7.

The bundled collection contracts are pinned and must remain unchanged. Offline
tests do not establish live-game compatibility.

## License

[MIT](LICENSE)

The bundled Exportaramus Consumer Kit uses the same MIT license. See
[third-party notices](THIRD-PARTY-NOTICES.md) for its source and attribution.
