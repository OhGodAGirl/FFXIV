# Consumer Integration Guide

This guide explains how to create an FFXIV plugin that uses data collected by Exportaramus. You will add commands that connect to Exportaramus, read recorded collections and inventory, and display results in the game's chat. It also explains how to keep a persistent display up to date when those results change.

Basic C# knowledge is assumed. No previous experience with FFXIV or Dalamud is required. A **consumer** is the plugin you build to use Exportaramus data; Exportaramus is its **provider**.

In this tutorial, the consumer is **Collection Example**. Its `collections` field holds a `CollectionDataIpcClient` object from the Consumer Kit. That object manages the connection and validates replies; it is what this guide calls the **client**. It runs inside your plugin, not as another installed plugin or game window.

| Part | Start Here When You Want To |
| --- | --- |
| [1. Tutorial](#1-tutorial) | Build and load the example, display a mount count, and query an item's quantities. |
| [2. Concepts](#2-concepts) | Understand what a result means, whether it is usable, and how connections recover. |
| [3. Integration Reference](#3-integration-reference) | Find the complete API definitions and add reporting, saved-file, or website-export support. |

For one feature, use [the collection example](#16-display-a-collection-count) or [the inventory example](#17-query-an-items-quantities), then consult the relevant concept or endpoint. The [complete example project](../examples/CollectionExample/CollectionExample.csproj) accompanies the tutorial. The [Backend Specification](<Exportaramus - Backend Specification.md>) provides the complete field definitions and provider behavior.

This guide targets **CollectionData 1.2** and **Inventory 1.0**. These are data-contract versions, independent of the Exportaramus plugin release. Inventory's literal IPC prefix is `Exportaramus.Inventory.V1.`; the `V1` spelling is part of the endpoint name. New integrations should use the versions shown here. The [contract-version reference](#34-contract-versions-and-schema-files) is for maintaining consumers of older contracts.

## 1. Tutorial

The example supplies two commands. **Mounts** are creatures or vehicles a character unlocks for travel. The first command counts recorded mount unlocks; the second looks up a physical item by its game-data ID.

| Command | Result |
| --- | --- |
| `/collectionexample` | The active character's recorded mount count. |
| `/collectionexample item <base-item-id>` | Last-observed item quantities and matching storage locations. |

These commands print a result when invoked. Old chat messages do not update. The connection refreshes its accepted collection data in the background, so running the command again can show a newer result. A window that stays open needs the update handling described in [Connection Lifetime](#27-connection-lifetime-and-shutdown).

**Where to enter commands:** type every command beginning with `/` into FFXIV's ordinary chat input while logged into a character, then press Enter. Clicking the chat input or pressing Enter normally focuses it. Dalamud handles its registered commands locally; these examples do not send a message to other players. The `dotnet` build commands later in the tutorial run in PowerShell, outside the game.

### 1.1 Prepare the Environment

You need a working Windows FFXIV installation with Dalamud enabled; begin with the official [Dalamud Getting Started guide](https://dalamud.dev/plugin-development/getting-started/) if you have not prepared a plugin environment. Dalamud is the framework that loads .NET plugins inside the game and supplies commands, updates, and UI services. XIVLauncher can load Dalamud when it launches FFXIV.

Use the .NET SDK selected by this development tree's `global.json`. This example targets .NET 10 and Dalamud API 15 through `Dalamud.NET.Sdk/15.0.0`. Its build must use compatible host assemblies; these are the libraries against which the plugin compiles. The versions identify this example's target, not a promise of compatibility with every later Dalamud update.

You also need Exportaramus installed and enabled. A built DLL on disk is not a running plugin; the following steps establish those separately.

**Example dependency:** CollectionData uses Consumer Kit 1.2, a library that connects to Exportaramus and validates its replies. Use a source checkout containing the kit project and example shown below. Installing Exportaramus in the game supplies the provider, not these development files.

Exportaramus offers two interfaces: **CollectionData** for collection unlocks and related evidence, and **Inventory** for item quantities and storage locations. Both describe the character's recorded data. A **contract** defines an interface's function names, arguments, response fields, and their meaning. Part 2 explains the distinction in more detail.

### 1.2 Install and Confirm Exportaramus

1. In game, run `/xlsettings` to open Dalamud Settings.
2. Under **Experimental → Custom Plugin Repositories**, add the following repository URL with **+**, then select **Save**:

   ```text
   https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/pluginmaster.json
   ```

3. Open `/xlplugins`, search for **Exportaramus** under All Plugins, and select **Install**. If it is already installed, check that its installed entry is enabled.
4. Log into a character and run `/exportaramus`. Its main window opening confirms that its command is running.
5. Select **Sync Now**. If some data is unavailable, hover over Snapshot Health to identify the affected source. A working command does not prove that every source has been captured.

The [player README](../README.md#installation) contains the same installation route. If `/exportaramus` is unknown, check the installed entry for a disabled or failed load before debugging your consumer. Installing the consumer does not install or enable Exportaramus automatically.

### 1.3 Project Files

A **schema** defines the permitted structure of a JSON response. The kit bundles its schemas with the client that checks them.

Start with the complete supplied project. Build it unchanged once, then edit its existing files. You do not need to invent filenames or assemble the later excerpts into a new class.

```text
Exportaramus/
  examples/CollectionExample/
    CollectionExample.csproj
    packages.lock.json
    Plugin.Dalamud.cs
    InventoryItemReader.cs
  project/ConsumerKit/1.2/
    Exportaramus.ConsumerKit.V1_2.csproj
  src/Contracts/1.2/
    ...shared data types and validators
    Consumer/
      CollectionDataIpcClient.cs
      CollectionDataGateBindings.Dalamud.cs
  data/Contracts/1.2/
    schemas/
    resources/
```

If you move the example into your own repository, keep the project, both C# files, and `packages.lock.json` together and set `ConsumerKitPath` as described below. The lock file records the package versions used by the locked restore command. The project already names its source files explicitly. Do not create a second `Plugin.cs` or duplicate the entry point.

| File | Purpose |
| :--- | :--- |
| [CollectionExample.csproj](../examples/CollectionExample/CollectionExample.csproj) | Pins the plugin SDK, defines plugin metadata, and references the kit. |
| [Plugin.Dalamud.cs](../examples/CollectionExample/Plugin.Dalamud.cs) | Owns the connection, command, update callback, and shutdown. |
| [InventoryItemReader.cs](../examples/CollectionExample/InventoryItemReader.cs) | Validates an Inventory V1 item response and returns typed values. |
| [Consumer Kit 1.2](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>) | CollectionData client, data types, schemas, and supporting resources. |

The example project uses the Exportaramus source root as `ConsumerKitPath`. It finds the kit project at `project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj`, its shared types under `src/Contracts/1.2/`, and its connection files under `src/Contracts/1.2/Consumer/`. If the example is in another repository, set `ConsumerKitPath` to that Exportaramus root. A missing project or binding file produces a build error naming the missing dependency.

Keep the kit project, source, schemas, and supporting data from the same source revision. Moving only the project file omits files it needs to build.

Reference `project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj`. The provider project at `project/Contracts/1.2/Exportaramus.ConsumerKit.V1_2.Runtime.csproj` excludes the consumer client. Include `CollectionDataGateBindings.Dalamud.cs` in the plugin assembly; the project already does this. Do not reference `Exportaramus.dll`.

### 1.4 Build the Example

Run these commands from the Exportaramus development root. Set `DALAMUD_HOME` to the installed API 15 assembly directory; the path below is the reviewed environment.

```powershell
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncher\addon\Hooks\15.0.3.9"

dotnet restore examples/CollectionExample/CollectionExample.csproj --artifacts-path examples/CollectionExample/artifacts --locked-mode
dotnet build examples/CollectionExample/CollectionExample.csproj -c Release --artifacts-path examples/CollectionExample/artifacts --no-restore
```

For a separate copy of the example, pass the Exportaramus source root on both restore and build, for example `-p:ConsumerKitPath="C:/Projects/Exportaramus"`.

The output directory is `examples/CollectionExample/artifacts/bin/CollectionExample/release/`. The shared artifacts path keeps generated files out of the versioned kit. It must contain `CollectionExample.dll`, `CollectionExample.json`, and `Exportaramus.ConsumerKit.V1_2.dll`. Keep the dependency DLL beside the plugin DLL.

A successful build establishes that the code compiles against the selected API. It does not establish that Dalamud loaded the plugin.

### 1.5 Register and Confirm the Example

1. Confirm the build output contains `CollectionExample.dll`, its adjacent `CollectionExample.json` manifest, and `Exportaramus.ConsumerKit.V1_2.dll`. The manifest describes the plugin to Dalamud; the dependency DLL supplies the collection client.
2. Run `/xlsettings`. Under **Experimental → Dev Plugin Locations**, add the full absolute path to `CollectionExample.dll`, select **+**, and save. Register the plugin DLL itself so the sibling dependency is not treated as a separate plugin.
3. Open `/xlplugins` and select **Dev Tools → Installed Dev Plugins**. Find **Collection Example** and enable it. Check that the entry points to the output you just built and has no load error.
4. With a character logged in and Exportaramus enabled, run `/collectionexample`.

For the registration mechanism, see Dalamud's official [development-plugin location instructions](https://dalamud.dev/faq/getting-started/#how-do-i-hot-reload-my-plugin). That page is marked as legacy documentation; UI labels can differ between host versions.

| What You See | What It Establishes | Next Check |
| --- | --- | --- |
| The command is unknown. | The example's command is not registered. | Check its development entry, enabled state, load error, manifest, and dependency DLL. |
| `Log into a character first.` | The example loaded and handled the command, but has no active character. | Log in and run it again. |
| Collection data unavailable or refreshing. | The command ran, but the client has not accepted a usable reply. | Confirm Exportaramus is enabled, let Sync Now finish, allow the next discovery interval, then retry. |
| A current, complete mount observation is unavailable. | The connection accepted data, but mount-source evidence is unsuitable. | Check Exportaramus' source status; do not replace this with a zero count. |
| `Exportaramus recorded <n> unlocked mounts.` | The example accepted a complete mount observation for the active character. | Compare against the character's mount collection. |

Do not equate a successful build, an installed entry, an enabled plugin, and an accepted data source. Each checks a different part of the integration.

#### 1.5.1 Refresh the Data and Repeat the Command

Here, **refresh Exportaramus** means asking it to collect data again. It does not mean rebuilding a DLL, reloading either plugin, or restarting FFXIV.

1. Enter `/collectionexample` in game chat and note its result.
2. Enter `/exportaramus` to open the provider's window, then select **Sync Now**. Let synchronization finish. If data remains unavailable, hover over Snapshot Health for the relevant explanation.
3. Allow the example's connection to process the new data. Its fallback check runs at five-second intervals while the game updates; that interval is not a guarantee that capture or validation has finished.
4. Enter `/collectionexample` again in game chat. Read the new chat message. The earlier message will not change, and the count may legitimately be the same if no unlocks changed.

For a stored-item query, repeat the same `/collectionexample item <base-item-id>` command after refreshing the relevant storage. Storage managed by hired assistants (**Retainers**) requires visiting a Summoning Bell and using Exportaramus' Retainers action. Appearance storage (**Glamour Dresser** and **Armoire**) uses Dresser & Armoire. Those game labels identify the buttons and storage systems; ordinary Sync Now cannot guarantee that unopened storage was read.

#### 1.5.2 Confirm Disconnect and Reconnect Handling

Leave **Collection Example enabled** throughout this test:

1. Open `/xlplugins`, select **Installed Plugins**, and find **Exportaramus**. If testing a development copy, find that active copy under Dev Tools instead.
2. Turn Exportaramus off with its enable/disable toggle. Confirm the entry shows it as disabled. Do not disable Collection Example and do not uninstall either plugin.
3. Let the running example process the withdrawal or its next connection check, then enter `/collectionexample` again. Expect `Collection data is unavailable or refreshing. Check Exportaramus and try again.` rather than a new usable count. If the first command raced the transition, wait for the next check and repeat.
4. The kit represents the disconnected state through `collections.View.Availability`, normally `Unavailable` after the failed connection check. Inspect that property in a debugger if you need to distinguish it from the example's shared unavailable/refreshing chat message. The example does not print an unsolicited disconnect notification.
5. Enable Exportaramus again, open it with `/exportaramus`, and select Sync Now if needed. Leave Collection Example running. After discovery and acceptance, repeat `/collectionexample`; a usable complete observation restores the count output.

If the command itself becomes unknown, the consumer was unloaded or its command was not registered; that is a different failure from detecting the provider's absence. Old count messages left in chat are historical output, not evidence that the client still accepts them.

### 1.6 Display a Collection Count

A **snapshot** is a set of observations published together; its sources may have been read at different times. **IPC** means inter-plugin communication: Dalamud functions and events used by one plugin to reach another. A named function or event is a **call gate**. A **contract** specifies those names, argument types, response fields, and their meaning. **Capabilities** describe the contract supported by a running provider.

The kit handles those details for CollectionData. The following steps explain the code in `Plugin.Dalamud.cs`; they are not additional code to paste into the supplied project. The example's **framework update** is a short callback that Dalamud runs as the game updates.

#### 1.6.1 Establish the Connection

In [Plugin.Dalamud.cs](../examples/CollectionExample/Plugin.Dalamud.cs), the `Plugin` class owns a `private readonly CollectionDataIpcClient collections;` field. The following excerpt belongs in that class's `Plugin(...)` constructor and is already present in the supplied project. It creates the client and gives it a factory for the host connection. **Read this as an explanation of the existing file; do not paste a second copy into a new file.**

```csharp
collections = new((changed, withdrawn) =>
    new CollectionDataGateBindings(pluginInterface, changed, withdrawn));
```

`pluginInterface` is the constructor parameter supplied by Dalamud. `new(...)` uses the field's declared type, `CollectionDataIpcClient`. The `changed` and `withdrawn` arguments are callbacks created by that client; `CollectionDataGateBindings` subscribes them to Exportaramus' events. Your plugin owns the client; the client owns those connection bindings.

The binding subscribes before the first read. This avoids relying on a future event to discover data that was already published.

Creating a subscriber does not prove Exportaramus is running. Missing functions and failed calls must produce an unavailable state.

#### 1.6.2 Supply Character Identity and Update the Client

A **Content ID** is FFXIV's numeric character identifier. In `Plugin.Dalamud.cs`, `UpdateCharacter()` obtains the expected ID from Dalamud and stores it in `activeCharacter`. Your client compares this value with the ID in Exportaramus' reply; it does not trust the reply to identify the active character. `OnUpdate(IFramework _)` then executes this existing line:

```csharp
refresh = collections.TickAsync(activeCharacter, DateTimeOffset.UtcNow);
```

`activeCharacter` is a nullable string field, and `refresh` is a `Task` field on `Plugin`. Pass a nonzero decimal Content ID, or null when no character is available. Retain the returned task and observe failures. The constructor registers `OnUpdate` with `framework.Update`; do not call it manually from the command or window draw code.

The client combines repeated change notifications and performs routine discovery at five-second intervals. Calling `TickAsync` each update does not read a snapshot every frame.

Passing null means no active character is available. The client will not accept a character snapshot in that state; null does not request global or account-wide data. Capabilities describe the interface, not a substitute character snapshot.

The example checks Dalamud's `IPlayerState` service on every framework update. This existing method belongs to `Plugin.Dalamud.cs`:

```csharp
private void UpdateCharacter()
{
    var id = player.IsLoaded ? player.ContentId : 0;
    var current = id == 0 ? null : id.ToString(CultureInfo.InvariantCulture);
    if (current == activeCharacter) return;
    activeCharacter = current;
    characterGeneration++;
    requestedItem = null;
}
```

Here `player` is the injected `IPlayerState` service. Logging out changes `current` to null; logging into a different character changes its ID. The example increments `characterGeneration`, a local request counter, at each detected transition. An inventory result is displayed only if its saved counter still matches. The collection client separately discards work for an earlier character. This prevents a delayed response from appearing under the wrong character.

#### 1.6.3 Accept the Result

In `Plugin.Dalamud.cs`, the no-argument branch of `OnCommand(string command, string arguments)` reads `var view = collections.View;`. `View` is a read-only **property of the client object**, not an API method, a window, or a property of the snapshot. It returns a `CollectionDataClientView`, which describes the client's latest accepted state. `view.Snapshot` holds its optional `CollectionDataSnapshot`.

In that method, the local variable `snapshot` is obtained from `view.Snapshot`; the local `health` is obtained from that snapshot's `SourceHealth` dictionary. Require all of the following before displaying the count:

| Check | Purpose |
| :--- | :--- |
| Availability is `Ready`. | The client accepted the response. |
| Snapshot Content ID matches the active character. | The display still belongs to the current character. |
| The required source exists. | Missing data must not become an empty collection. |
| `sourceHealth.mounts.sourceStatus` is `Fresh`, `completeness` is `Complete`, and `provenanceType` is `Observed`. | The example requires a current, complete game observation. |

These health fields say whether the mount observation is current under the collection contract, covers the complete source, and came from the game. In the typed kit, the corresponding properties are `SourceStatus`, `Completeness`, and `ProvenanceType`. Read them from `snapshot.SourceHealth["mounts"]`.

For the mount example, read `snapshot.Unlocks["mounts"].UnlockedIds` only after those checks. Its length is the recorded mount count.

`Ready` applies to the accepted document. It does not guarantee that every source is available or that all sources were read simultaneously.

See [Client and Snapshot Objects](#37-client-and-snapshot-objects) for every property on the client view and snapshot, and [Source Metadata](#38-source-metadata) for the health/capability fields and literal status values. Use those definitions when implementing a different feature; do not infer a property's meaning from its name.

#### 1.6.4 Display States

| Client State | Display Behavior |
| :--- | :--- |
| `Unavailable` | Explain that data is unavailable; retain the ability to reconnect. |
| `Refreshing` | Show that a refresh is in progress, or retain a separately labelled previous observation. |
| `Ready` | Check source health, then display accepted facts. |
| `Rejected` | The client declined the reply after validation. Stop displaying it as current; use [`Failure`](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>) for diagnosis. |
| `Disposed` | Do not schedule or display further results from the client. |

The example prints to chat. A window should prepare display values during updates and render those values during draw.

### 1.7 Query an Item's Quantities

The example supports `/collectionexample item <base-item-id>`. A game **sheet** is a table of definitions. The `Item` sheet lists physical items; each row's `RowId` is that item's base ID. This is different from an ID in a collection such as mounts.

To obtain an ID in your plugin, request Dalamud's [`IDataManager` service](#251-obtaining-a-catalog), call `GetExcelSheet<Lumina.Excel.Sheets.Item>()`, and use the selected row's `RowId`. Present the row's `Name` when building an item picker. Names are localized and need not uniquely identify an item, so keep the selected ID rather than sending its name to IPC. This is an optional picker you would add to your plugin; the supplied command expects a numeric ID you have already selected.

Some game interfaces encode high quality in the numeric item identifier. An **HQ-encoded ID** is that quality-marked value, not the base row ID accepted by `GetItem`. For native inventory slots, the provider obtains the base ID through `GetBaseItemId()`; see its [inventory adapter](../src/Features/Inventory/InventoryContainerReader.Dalamud.cs). Use the documented normalization for the API supplying your input. Do not pass an encoded ID or assume every game identifier has the same encoding.

Items can have normal-quality or high-quality (**HQ**) copies. A base item ID identifies the item independently of that quality. **Retainers** are game-controlled assistants employed by a character, each with separate storage. The Armoire and Glamour Dresser are storage systems for eligible equipment and appearances. The query can return these locations even when they were observed at different times.

For example, a result might report two normal-quality Rubies in the character's inventory and three on one retainer: five last-observed Rubies in total. That is not proof that the total is still five now. The state and timestamps tell the consumer how to label it; see [Inventory Meaning](#26-inventory-meaning).

The command queues one request. The next update starts a worker that:

1. Reads Inventory capabilities.
2. Calls `GetItem` with the requested base item ID.
3. Validates both responses.
4. Returns a typed result or an unavailable result.

The framework update displays the result only if the character has remained in the same login sequence. The example increments a local generation counter whenever the active character changes, including logout. This rejects an old response even if the player later logs back into the same character.

The Inventory connection is also created in the `Plugin(...)` constructor in `Plugin.Dalamud.cs`. This existing excerpt binds its capabilities and item functions:

```csharp
var caps = pluginInterface.GetIpcSubscriber<string>("Exportaramus.Inventory.V1.GetCapabilities");
var item = pluginInterface.GetIpcSubscriber<uint, string>("Exportaramus.Inventory.V1.GetItem");
inventoryCapabilities = () => caps.InvokeFunc();
inventoryItem = id => item.InvokeFunc(id);
```

`inventoryItem` is the class's `Func<uint, string>` field. `OnCommand` records the requested item ID; `OnUpdate` invokes that delegate inside its one lookup worker and passes the returned JSON to `InventoryItemReader.Read`. Keep that request/validation flow together. This excerpt is not an instruction to call IPC from a window's draw method or to bypass the parser.

There are two distinct failure paths. If Exportaramus is absent or its IPC function fails, `InvokeFunc()` can throw before returning any JSON. The example's worker task records the exception; `OnUpdate` catches it, logs the failure, and prints an unavailable message. If a call succeeds, its JSON may instead say `available: false` or fail validation. Neither case supplies a current item result. The example does not reuse a previous query after failure. See the [Inventory response reference](#32-inventory-10) for exact response shapes.

#### 1.7.1 Validate the Response

[InventoryItemReader.cs](../examples/CollectionExample/InventoryItemReader.cs) implements the following checks:

| Area | Required Check |
| :--- | :--- |
| Capabilities | Schema `1.0`, scope `CurrentCharacter`, Normal/HighQuality quantities, and `retainedIsCurrent: false`. |
| Input | Expected character ID and requested base item ID are valid. |
| JSON structure | Required fields and types are present; duplicate and unexpected fields are rejected. |
| Available response | Character and item IDs match the request; revision is positive. |
| Locations | Known kinds, distinct `(kind, retainerId)` keys, valid names and observation states. |
| Observation time | Each matching location has a valid UTC observation time. |
| Quantities | Exact integer counts; null quantities only for membership-only locations. |
| Totals | Reported totals equal the sum of quantified matches. |
| Unavailable response | Null character, revision zero, empty matches, zero totals, and unknown coverage. |

The parser limits responses to **4 MiB**, JSON depth to **16**, and matches to **17 locations**: seven non-retainer kinds and at most ten retainers. These are explicit limits of the example; Inventory capabilities do not advertise parser limits.

The parser is strict for the currently described response. If the contract changes, review its field checks rather than silently accepting new meanings.

It validates one item response. It is not a full-snapshot parser, an automatic inventory subscription client, or a substitute for checking current native state before item actions.

### 1.8 Verify the Consumer

#### 1.8.1 Automated Checks

Verify these conditions using controlled responses:

| Condition | Expected Result | Reference |
| --- | --- | --- |
| Complete empty collection | Accepted empty observation. | [Unlock Decisions](#252-unlocked-locked-and-unknown) |
| Missing or incomplete source | Unknown, not empty. | [Source Health](<Exportaramus - Backend Specification.md#42-source-health>) |
| Wrong character or provider session | Client rejects the response. | [Identity](#162-supply-character-identity-and-update-the-client), [Ordering](#271-response-ordering) |
| Lower collection revision or conflicting equal revision | Client rejects the response. | [Ordering](#271-response-ordering) |
| Malformed, oversized, unsupported, or contradictory JSON | No accepted current result. | [Validation](#171-validate-the-response), [Contract Definitions](#34-contract-versions-and-schema-files) |
| Normal-quality and high-quality quantities | Separate exact counts; totals may require `UInt64`. | [Item Response](<Exportaramus - Backend Specification.md#144-item-query-fields>) |
| Armoire or Dresser match | Null quantities, excluded from totals. | [Quantities and Membership](#261-quantities-and-membership) |
| Unavailable inventory query | Unavailable result, not an empty-inventory claim. | [Inventory Outcomes](#32-inventory-10) |
| Retained contents or a failed later read | Original observation age remains visible. | [Observation States](#262-observation-states-and-coverage) |
| Character change during a request | Delayed result discarded. | [Character Tracking](#162-supply-character-identity-and-update-the-client) |
| Callback burst or unload during work | No duplicate subscriptions or abandoned work. | [Notifications](#272-notifications-and-recovery), [Shutdown](#273-shutdown-order) |

#### 1.8.2 In-Game Checks

- Start the consumer before Exportaramus; then enable Exportaramus.
- Start the consumer after Exportaramus has already captured data.
- Refresh Exportaramus and verify a later command uses the new result.
- [Disable and re-enable Exportaramus](#152-confirm-disconnect-and-reconnect-handling) without reloading the consumer.
- [Log out or switch character](#162-supply-character-identity-and-update-the-client) while a request is pending.
- Query an item with normal-quality and high-quality stacks.
- [Query an item held by a retainer](#17-query-an-items-quantities); compare name, quantity, and [observation age](#262-observation-states-and-coverage).
- Check unread storage and membership-only storage.
- [Unload the example](#273-shutdown-order) and confirm its command is removed.

A successful build verifies API use. Automated checks verify the tested response handling. In-game checks establish behavior in the running host; report those stages separately.

## 2. Concepts

Use this part to decide what your feature can conclude from an accepted response. A successful IPC call and a readable JSON document are only the start of that decision.

### 2.1 Providers, Readers, and Consumers

Exportaramus is the provider. Your plugin is the consumer. A **reader** is Exportaramus code that obtains an observation from the game. A **source** is the named group of data whose observation Exportaramus tracks separately: for example, travel unlocks (`mounts`) or carried items (`inventory`). Readers produce observations for sources. A source key names the data, not the code that reads it.

Source keys appear in `snapshot.SourceHealth` and `snapshot.SourceCapabilities`. For example, `snapshot.SourceHealth["mounts"]` describes the mount observation, while `snapshot.SourceCapabilities["mounts"]` describes support for obtaining that observation. Check that a key exists before indexing it. The [source metadata reference](#38-source-metadata) lists the fields and permitted status values.

Provider availability and reader availability are different:

| Situation | Meaning | Consumer Behavior |
| --- | --- | --- |
| Exportaramus is absent, disabled, or its call fails. | There is no usable provider connection for this request. | Keep the rest of your plugin usable, explain which feature requires Exportaramus, and allow reconnection. |
| Exportaramus responds, but a required reader has no usable observation. | The provider is running; that source is unavailable, unread, incomplete, or rejected. | Keep unrelated features usable. Label the affected result unavailable or unknown and show its source-specific explanation when available. |
| Exportaramus responds with retained observations. | Earlier data is available but does not establish the current game state. | Show its age and historical status, or decline to use it when the feature needs current evidence. |

A consumer checks the source metadata returned by Exportaramus. It does not inspect or restart readers itself. For example, unavailable collectible lore records (Field Records) need not disable a feature that uses a complete travel-unlock observation (mounts). No capture or refresh IPC endpoint is provided.

Dalamud calls the provider's named functions and delivers its notifications through call gates. See the official [IPC interfaces](https://dalamud.dev/api/Dalamud.Plugin.Ipc/) and [`GetIpcSubscriber` API](https://dalamud.dev/api/Dalamud.Plugin/Interfaces/IDalamudPluginInterface/) for the host mechanism.

### 2.2 Choose CollectionData or Inventory

Both interfaces describe recorded data for a character. CollectionData answers collection-progress questions; Inventory answers physical-item questions. A **catalog** lists the entries that exist in a collection, with their IDs and names. It supplies the definitions needed to display a **checklist**: a row for each entry and whether the character has unlocked it. [Obtaining a Catalog](#251-obtaining-a-catalog) explains where those definitions come from.

A character can obtain an item that unlocks a collectible. **Acquisition** means obtaining the item. **Item presence** means an observation found it in storage. Using the item may remove—**consume**—it and permanently register the collectible as **unlocked**. A **locked** collectible has not been registered as unlocked; **unknown** means the available data cannot establish either result. These meanings are distinct from whether the item itself is currently in inventory.

An optional installed plugin may report a confirmed acquisition to Exportaramus. It could be your consumer or a different plugin that observed the result. Such a reporter is not required by this tutorial. Reports are stored separately from game observations; [Acquisition Reports](#33-acquisition-reports) explains their purpose and limits.

| Interface | What It Describes | Example Question |
| --- | --- | --- |
| CollectionData | Permanent collection unlocks, item presence in supported storage sources, and acquisitions reported by other plugins. | Which mounts has this character unlocked? |
| Inventory | Item quantities separated by quality, with individual storage locations and observation states. | How many Rubies were last observed, and which retainer holds them? |

Unlocking a mount and holding its summoning item are different facts: the character can keep the permanent unlock after the item is consumed. CollectionData reports unlock evidence; Inventory answers physical-item quantity and location questions. CollectionData also includes storage-presence evidence, but use Inventory when you need individual retainer attribution and normal-/high-quality totals.

Personal storage assistants (**retainers**) are NPCs employed by a character, each with separate storage. **NPC** means a game-controlled character. Special equipment storage (**Armoire**) holds eligible clothing and gear; appearance storage (**Glamour Dresser**) holds equipment for applying its look to other equipment. Exportaramus may need the player to visit those storage systems before their contents are recorded.

Observations carry status fields. In CollectionData JSON, these include `sourceHealth.<source key>.sourceStatus`, `completeness`, and `provenanceType`. The kit exposes the corresponding C# properties as `SourceStatus`, `Completeness`, and `ProvenanceType`. Inventory instead uses each location's `state` and timestamps. These are different contracts, not one interchangeable observation-status value. Their interpretation is covered below.

### 2.3 Identity and Observation Terms

| Term | Meaning |
| :--- | :--- |
| Content ID | FFXIV's numeric character identifier. Read it from Dalamud and compare it with Exportaramus' reply; do not trust the reply to identify the active character. |
| Snapshot | Observations published together. Individual sources may have been read at different times. |
| Capabilities | A description of the supported contract, sources, and applicable limits. |
| Revision | A number used to order publications. It is not a timestamp. |
| Provider Session ID | Identifies one running CollectionData provider session. Inventory 1.0 does not supply this field. |
| Source Health | Whether a source was read successfully, whether the observation is complete, and when it was observed. |
| Capture Generation | Identifies the capture associated with an observation or source status. A generation number alone does not prove a successful read. |
| Provenance | How a fact was obtained: for example, observed in the game, restored from storage, or reported by another plugin. |

Keep Content IDs as exact decimal strings or unsigned 64-bit integers. Floating-point numbers cannot represent every possible identifier exactly.

### 2.4 Runtime and Consumer Responsibilities

Dalamud loads .NET plugin assemblies inside the FFXIV process. It constructs the plugin entry point and supplies services through its constructor.

The example uses these services:

| Service | Responsibility |
| :--- | :--- |
| `IDalamudPluginInterface` | Obtain connections to Exportaramus' IPC functions and events. |
| `IFramework` | Run a short update callback as the game updates. |
| `IPlayerState` | Identify the character currently logged in. |
| `ICommandManager` | Register and remove the example command. |
| `IChatGui` | Print results to local chat. |
| `IPluginLog` | Record failures for diagnosis. |

The **framework thread** is the thread on which Dalamud runs its regular game-update callback. Read active character state there. Perform JSON parsing and other substantial processing on a worker after copying the necessary values.

A window's draw callback runs repeatedly to render the interface. It should display prepared results rather than call IPC functions or parse JSON.

Separate three responsibilities:

| Component | Responsibility | Reason |
| :--- | :--- | :--- |
| Dalamud connection | Obtain services, register callbacks, and identify the active character. | Game and plugin lifetime belong to the host. |
| Client and validation | Read responses, check identity and format, and reject outdated work. | A readable JSON document can still describe the wrong character or contradictory facts. |
| Presentation | Convert accepted data into text, counts, or rows. | Display code should not repeat connection or validation logic. |

The normal sequence is:

1. Determine the active character.
2. Establish the connection and subscribe to relevant events.
3. Read and validate capabilities.
4. Read and validate data for that character.
5. Check the required source's health.
6. Prepare the display.
7. Repeat when notified of a change or when checking for a provider that was previously unavailable.

For CollectionData, there are two implementation choices: use the supplied [Consumer Kit](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>), as this tutorial does, or write your own IPC connection and validators. The kit handles subscriptions, periodic reads, response validation, and connection state. Your plugin supplies the active character, calls `TickAsync`, checks the selected source before displaying it, and disposes the client. If you replace the kit, implement the [same lifetime and validation rules](#27-connection-lifetime-and-shutdown).

Inventory has a separate contract. The tutorial supplies [`InventoryItemReader`](../examples/CollectionExample/InventoryItemReader.cs) for one item query. A persistent inventory window also needs [its own update and reconnection handling](#274-persistent-inventory-displays).

In this guide, a **feature** means a function of your plugin, such as its mount count or item search. Data is **usable for that feature** only after response validation and the feature's own source checks pass. For example, the tutorial's mount count requires a fresh, complete game observation; a historical inventory display can show an older observation if it labels the age. There is no general `usable` field in the response.

Required behavior:

- Connect when Exportaramus is available, including when it loads after the consumer.
- Accept data only for the active character and supported contract.
- Distinguish current observations, earlier observations, and unavailable data.
- Refresh a persistent display when its underlying data changes.
- Discard responses belonging to an earlier character or connection.
- Release commands, subscriptions, and background work when the consumer unloads.
- Remain usable when Exportaramus is absent.

Out of scope:

The integration does not implement character-unlock readers, open storage windows, or instruct Exportaramus to capture a source. Reading static game definitions to label collection entries is separate from observing character state. No capture or refresh IPC endpoint is provided.

Collection and inventory observations may support planning. A plugin that buys, discards, moves, or equips items must verify the relevant game state at the time of the action. An earlier Exportaramus observation is insufficient for that decision.

The example prints observations to chat. Its previous messages are historical output; it does not maintain a live inventory window.

### 2.5 Collection Meaning

To display a collection checklist, your plugin needs two things: a catalog of the entries in that collection, and the character's unlock observations from Exportaramus. The catalog supplies IDs and names; the observations identify unlocked IDs and describe how reliably they were read. Comparing them produces the checklist.

For this display, **Unlocked** means the character was observed to have obtained the entry. **Locked** means a complete, current observation establishes that they have not. **Unknown** means the available evidence cannot settle the question. These are display decisions your plugin makes from the response, not a `Locked` or `Unlocked` property on the compact IPC snapshot.

#### 2.5.1 Obtaining a Catalog

The count tutorial needs no catalog: it counts the accepted IDs in `snapshot.Unlocks["mounts"].UnlockedIds`. To extend it into a named checklist, obtain definitions for mounts only. You do not need to type a list of every game item.

| Route | What It Supplies | What Your Plugin Must Do |
| --- | --- | --- |
| Installed game tables through Dalamud | Definitions such as mount IDs and localized names. | Read the chosen table, apply that collection's ID and eligibility rules, and compare with Exportaramus' observations. |
| Exportaramus' saved expanded snapshot | Names, entries, and ownership information for collections included in that file. | Validate the file, inspect omitted collections and observation age, and use its documented fields. See [Saved Snapshots](#35-saved-snapshots). |
| Consumer Kit 1.2 | Connection helpers, validators, schemas, and limited reviewed mapping resources. | Obtain the named catalog separately. The kit has no catalog-fetching function. |

**For an in-game mount checklist, use the first route.** Dalamud's [`IDataManager`](https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IDataManager/) service provides access to the installed game's tables through `GetExcelSheet<T>()`. These are game-data tables, often called *sheets*; no spreadsheet application or separate download is needed. Dalamud supplies access to the definitions. Your consumer selects and stores the entries it needs.

To extend the supplied example:

1. Add an `IDataManager dataManager` parameter to the `Plugin` constructor in `Plugin.Dalamud.cs`, alongside its existing Dalamud services. Store it if your update callback will build the catalog. The interface is in `Dalamud.Plugin.Services`.
2. Read `dataManager.GetExcelSheet<Mount>()`, using `Mount` from `Lumina.Excel.Sheets`. Copy each accepted row's ID and name into your own dictionary, keyed by `uint`. Perform this setup once per game-data version and language, not on every draw or command.
3. Apply the rules below. They match Exportaramus' mount catalog; retaining placeholder or unused rows would produce misleading locked entries.
4. Use each dictionary key to look up membership in the accepted `UnlockedIds` set. Apply the observation checks in the next subsection before assigning an unlock state.

| Mount Table Field | Use |
| --- | --- |
| `RowId` | Collection ID. Require a nonzero value. This is the ID returned in `Unlocks["mounts"]`, not the item ID of an unlock consumable. |
| `Singular.ToString()` | Localized display name. Require a nonempty value. |
| `Order` | Require a nonnegative value. |
| `Icon` | Require a nonzero value. |

Build from the same installed game data used by the running Exportaramus instance. If you persist the dictionary or combine it with exported files, record and compare game-data version and language before reusing it. A changed version requires rebuilding the catalog; an unreadable table leaves the checklist unavailable. Do not reinterpret a failed table read as an empty collection. For larger catalogs, copy definitions in bounded framework-update slices, following [Backend Specification, Section 5](<Exportaramus - Backend Specification.md#5-collection-catalog>).

The current rules are implemented in [CollectionCatalogBuilder.Dalamud.cs](../src/Features/CollectionData/Catalog/CollectionCatalogBuilder.Dalamud.cs) and [CatalogRowPolicy.cs](../src/Features/CollectionData/Catalog/CatalogRowPolicy.cs). Other collections use different tables and sometimes transform their IDs. Use [the identifier rules](<Exportaramus - Backend Specification.md#52-identifier-and-row-rules>) for the collection you add; do not assume every collection uses its table's row ID unchanged.

CollectionData's `GetSnapshot` does not return a named catalog. Its capabilities list collection keys such as `mounts`, not every entry in those collections. The kit's `resources/recognized-ids.json` is an FFXIV Collect import allowlist with numeric IDs; it has no names and does not include mounts. It cannot supply this checklist.

#### 2.5.2 Unlocked, Locked, and Unknown

First require an accepted response for the active character and a matching catalog. Then inspect the selected source's health, such as `snapshot.SourceHealth["mounts"]`. In the table below, a current observation means `SourceStatus == "Fresh"` and `ProvenanceType == "Observed"`; a complete observation also has `Completeness == "Complete"`.

| Evidence for a Recognized ID | Result |
| :--- | :--- |
| Fresh, complete observation includes the ID. | Unlocked |
| Fresh, complete observation excludes the ID. | Locked |
| Missing, incomplete, unavailable, or rejected observation. | Unknown |
| Only a stale observation exists. | Label it historical, or show Unknown if the feature requires current information. |

For example, if the matching catalog contains three entries and a complete, current observation contains two of their IDs, display those two as Unlocked and the remaining entry as Locked. If the observation is unavailable, those same catalog entries are Unknown. Their presence in a catalog tells you what they are, not whether the character has them.

An ID outside the matching catalog remains unknown. A stale observation can support an explicitly historical result, but must not be silently presented as current. See [Source Metadata](#38-source-metadata) for the status fields.

#### 2.5.3 Different Forms of Ownership Information

A consumer must distinguish the following evidence. The same item can be acquired, stored, and later consumed without those facts contradicting each other.

| Fact | Meaning |
| :--- | :--- |
| Permanent unlock | The game was observed to have registered the collection entry. |
| Item presence | The item was observed in a storage source. |
| Reported acquisition | An installed plugin submitted a claim that this character obtained the item. This may be your plugin; see [reporting](#33-acquisition-reports). |

Acquiring an unlock item does not prove that the character used it. Presence does not prove continued possession. Keep these facts separate.

CollectionData presence quantities are nullable and do not distinguish high-quality counts. Inventory provides the location-attributed quantity interface.

#### 2.5.4 Facewear, Dresser Entries, Beasts, and Retainers

Cosmetic eyewear (**facewear**) uses collection IDs derived from `GlassesStyle.RowId + 1`. `GlassesStyle` names a table in the game's static data; `RowId` is the numeric key of one row. It is not a property on an Exportaramus snapshot. The provider groups `Glasses` rows by their style and applies this conversion; see [FacewearMappingPolicy](../src/Features/CollectionData/Catalog/FacewearMappingPolicy.cs). A consumer reading `snapshot.Unlocks["facewear"].UnlockedIds` already receives converted collection IDs: do not add one again.

| Identifier | Where It Comes From | Use |
| --- | --- | --- |
| `Item.RowId` | A row in the game-data `Item` sheet. | Base physical item ID for Inventory queries. |
| `GlassesStyle.RowId` | A row in the game-data `GlassesStyle` sheet. | Input to the provider's facewear-ID mapping. |
| `unlocks.facewear.unlockedIds` | CollectionData JSON; C# `snapshot.Unlocks["facewear"].UnlockedIds`. | Facewear collection IDs already mapped by Exportaramus. |
| `unlocks.beasts.unlockedIds` | CollectionData JSON; C# `snapshot.Unlocks["beasts"].UnlockedIds`. | Recognized Beast collection IDs. |

[Collection Object Fields](<Exportaramus - Backend Specification.md#137-typed-collection-values>) defines dresser-entry properties. [Catalog Acquisition](#251-obtaining-a-catalog) explains how to obtain static definitions. Only fields needed for these conversions are described here; game-data rows are not Exportaramus response objects.

Appearance-storage records (**dresser entries**) describe physical composition, including outfit components. An omitted list means unknown composition; an empty list means a complete empty observation. Preserve distinct physical entries even when they contain the same item.

The game's collectible pets (**Beasts**) use `unlocks.beasts.unlockedIds` and `sourceHealth.beasts`. Use the canonical recognized IDs in the kit's reviewed XBMPet mapping; these are not item or action IDs. Older CollectionData versions cannot answer Beast queries. Item reports cannot establish Beast unlocks.

CollectionData's `retainers` source aggregates accepted Inventory observations after confirming the active character's **retainer roster**, the list of retainers that character currently employs. Its timestamp is the oldest contributing observation. An old aggregate may remain stale while a replacement roster is only partly read. Use Inventory for individual retainer attribution.

### 2.6 Inventory Meaning

Inventory records observations of storage locations, which can be read at different times. The [response reference](#32-inventory-10) links to the exact JSON fields and response rules.

#### 2.6.1 Quantities and Membership

A location's kind determines whether Exportaramus records copy counts or only membership:

| Location Type | Quantity Meaning |
| :--- | :--- |
| Inventory, equipment, Armoury Chest, saddlebags, retainers | Separate normal-quality and high-quality counts. |
| Armoire and Glamour Dresser | Membership only; quantities are null. |

Per-location counts fit `UInt32`. Query totals use `UInt64`. Parse both as exact unsigned integers.

A membership match means the item was present, but does not establish a copy count. Exclude it from numeric totals. Do not turn null into zero or one, or count an outfit's dresser components as separate physical copies.

Item queries label totals `LastObservedQuantifiedLocationsOnly`. They may combine locations read at different times.

#### 2.6.2 Observation States and Coverage

Each Inventory location contains a `state` field with one of these values:

| State | Interpretation | What Your Plugin Should Do |
| --- | --- | --- |
| `Unknown` | No complete successful observation is available. | Show unknown contents. Do not infer an empty location or count it as zero known stock. |
| `Observed` | A complete observation was accepted during this run. | Show it as last observed, with its timestamp. This does not guarantee unchanged contents. |
| `Retained` | Earlier contents were preserved or restored. | Label them as older observations. Do not poll repeatedly to make them current. |
| `ReadFailed` | The latest attempt failed; an earlier successful observation may remain. | Show retained contents and their age if present, otherwise show unknown. Allow a later accepted observation to replace them. |

`observedAtUtc` is the UTC time of the successful contents observation, or null when none exists in a full snapshot. `attemptedAtUtc` is the latest read attempt's UTC time; it appears on full-snapshot locations and can also be null. A failed attempt can advance `attemptedAtUtc` without changing `observedAtUtc`. Item-query matches contain `observedAtUtc` but not `attemptedAtUtc`.

Calling `GetItem` or `GetSnapshot` reads Exportaramus' prepared data; it does not open storage or request a new game observation. To obtain unread retainer contents, the player must make that storage available to Exportaramus, for example through its Retainers capture action. Your consumer can read again after a notification or on its normal refresh schedule. Never busy-loop until the state says `Observed`.

A complete empty read replaces previous contents. Failure does not. Reading one retainer does not refresh the others.

The full snapshot and item query expose different coverage fields. A **roster** is the list of employed retainers; knowing that list does not mean every retainer's contents were read.

| Field | Interpretation |
| :--- | :--- |
| `retainerRosterKnown` | The full snapshot knows the character's current employed-retainer list. |
| `hasUnknownLocations` | An item query has missing observations or uncertain roster coverage. |
| `hasRetainedLocations` | Some successfully observed location has a state other than `Observed`, even if it does not contain the queried item. |

The example prints totals, each matching location's state and observation time, and coverage warnings. It cannot name every unread location from `GetItem` alone; use the full snapshot for that display.

Names are labels. Use `(kind, retainerId)` as location identity. Non-retainer locations use retainer ID `"0"`.

### 2.7 Connection Lifetime and Shutdown

#### 2.7.1 Response Ordering

CollectionData checks the character ID, provider session ID, and revision together. Within one session and character:

- Reject a lower revision.
- Reject different payloads carrying the same revision.
- Require the snapshot session to match capabilities.

Compare revisions only for the same character and provider session. If Exportaramus restarts, the new session has its own revision sequence; revision 1 in that session is not automatically older than revision 20 from the previous session. Discard responses from the previous connection. These checks prevent delayed work from replacing data accepted for the current connection.

The supplied kit performs these checks. If replacing it, retain equivalent validation, including schema identity, input sizes, required fields, recognized IDs, and relationships between fields.

A **schema** describes the permitted JSON structure. Valid structure alone is insufficient: a response can contain correctly typed fields but identify the wrong character or disagree with its own quantities.

#### 2.7.2 Notifications and Recovery

Exportaramus publishes a snapshot before sending a change notification. **Publication** means the provider has accepted the document and made it available to its getters. A notification asks consumers to read again; it is not the snapshot itself. The [CollectionData endpoints](#31-collectiondata-12) define both events, and the [notice schemas](#34-contract-versions-and-schema-files) define their fields.

The supplied kit subscribes to `SnapshotChanged` and `SnapshotWithdrawn` before its first read. Its callbacks queue work and return promptly; the worker reads and validates replies. Your plugin must keep calling `TickAsync` as described in [the tutorial](#162-supply-character-identity-and-update-the-client). If you implement your own connection, use the same subscribe-then-read order and keep callbacks short and nonthrowing.

A **withdrawal** tells consumers that previously published data is no longer available, for example after logout. The kit validates a withdrawal notice and matches its `ProviderSessionId` and `ContentId` to its accepted view before clearing that view. A delayed notice for another session or character must not clear the current display. Independent character checks still apply; do not rely on receiving every notice.

Notifications can be missed. The kit's periodic reads recover without requiring the player to restart the consumer. In a custom connection, a callback failure must not prevent future reads. Exportaramus' accepted snapshot remains available even if delivery of its notification fails; the failure does not undo the provider's data update.

When the client rejects a reply, it clears its accepted current result. `View.Failure` identifies the failed check. When data is withdrawn or the provider disappears, it also stops supplying a current result. Your plugin should stop presenting the affected value as current. It may keep a separately labelled historical view with its original age. If your plugin calculates something from current data, such as a shopping list, mark that calculation outdated and recalculate after accepting new data. See [Client View](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>) for availability and failure values.

#### 2.7.3 Shutdown Order

There are two different lifetimes. When **your plugin** unloads, Dalamud calls its `Dispose()` method; cleanup starts there, as shown in [Plugin.Dalamud.cs](../examples/CollectionExample/Plugin.Dalamud.cs). When **Exportaramus** unloads, your plugin can remain running: its client processes a withdrawal or encounters a failed IPC call during a later read, marks the result unavailable, and continues connection discovery. A withdrawal may also mean logout, so it is not by itself proof that the provider unloaded.

In your plugin's disposal method:

1. Stop new commands and update callbacks.
2. Invalidate pending character-specific work.
3. Finish or cancel owned workers.
4. Remove subscriptions and release resources.
5. Return from plugin disposal only after that work is settled.

The example uses synchronous plugin disposal to wait for the kit's asynchronous cleanup and its one inventory worker. Both workers call Exportaramus' managed getters and do not wait for framework callbacks.

That restriction matters: if a worker waits for the framework thread while disposal blocks that same thread, neither can finish. If you change the worker or add game-thread work, redesign shutdown before retaining the synchronous wait.

A **binding** here is the `CollectionDataGateBindings` object that holds Dalamud's callable IPC handles and event callbacks. It first subscribes the changed callback to `SnapshotChanged`, then the withdrawn callback to `SnapshotWithdrawn`. If the second subscription fails, it removes the first. Disposal removes both using the original delegates; it does not unregister Exportaramus' functions. The supplied kit owns this binding and cleans it up through `DisposeAsync`; do not add duplicate manual subscriptions. See the [binding implementation](../src/Contracts/1.2/Consumer/CollectionDataGateBindings.Dalamud.cs) when replacing the kit.

#### 2.7.4 Persistent Inventory Displays

A window that remains open needs additional lifecycle handling beyond the example's one-time chat result:

- Subscribe to Inventory `SnapshotChanged` and `SnapshotWithdrawn`.
- Queue a refresh and return promptly from callbacks.
- Parse responses outside draw and event callbacks.
- Clear usable results on withdrawal, failed calls, logout, or character change.
- Periodically check for a provider that loaded or restarted later.
- Unsubscribe with the original delegates during shutdown.

Inventory events contain full snapshot JSON. CollectionData events contain notices. Their parsers are not interchangeable.

Inventory 1.0 has no provider session ID. On a known disconnect or a revision that moves backwards, discard the old revision tracking and establish a new connection. A reload that occurs entirely between checks cannot reliably be identified from revision alone.

If combining an item query with a full snapshot, require matching character IDs and revisions. Otherwise, query again or present them as independent observations.

## 3. Integration Reference

The [Backend Specification](<Exportaramus - Backend Specification.md>) is the full API reference: endpoint signatures, object properties, validation rules, and embedded JSON schemas. This section points to those definitions and explains optional integration tasks. Use those definitions alongside the tutorial when adding a feature.

### 3.1 CollectionData 1.2

Use the [CollectionData Functions](<Exportaramus - Backend Specification.md#131-versions-and-functions>) for exact IPC names and subscriber types. The [Consumer Kit Client API](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>) defines connection methods, state, and shutdown. [Typed Collection Values](<Exportaramus - Backend Specification.md#137-typed-collection-values>) lists every record property, and the [Validation API](<Exportaramus - Backend Specification.md#138-validation-api>) defines accepted inputs and failure codes.

Collection change events are notices to reread the snapshot. They do not contain replacement collection data. See [Notifications](<Exportaramus - Backend Specification.md#133-notifications>) and [Connection Lifetime](#27-connection-lifetime-and-shutdown).

### 3.2 Inventory 1.0

Inventory describes the current character's recorded item quantities and storage locations. Its data is separate from collection unlocks, and the Consumer Kit's collection validator cannot validate it.

| Information Needed | Backend Reference |
| --- | --- |
| IPC names, arguments, event payloads, and invalid item IDs | [Functions and Events](<Exportaramus - Backend Specification.md#141-functions-and-events>) |
| Supported contract and quantity kinds | [Capabilities](<Exportaramus - Backend Specification.md#142-capabilities>) |
| Character, revision, locations, timestamps, and stored items | [Full Snapshot Fields](<Exportaramus - Backend Specification.md#143-full-snapshot-fields>) |
| One item's totals, quality, coverage, and matching locations | [Item Query Fields](<Exportaramus - Backend Specification.md#144-item-query-fields>) |
| Connection failures, response validation, and shutdown | [Consumer Lifetime and Safety](<Exportaramus - Backend Specification.md#145-consumer-lifetime-and-safety>) |
| Location kinds, bounds, observation states, and retainers | [Inventory Integration](<Exportaramus - Backend Specification.md#10-inventory-integration>) |

An invocation can fail before returning JSON. A returned response can instead be unavailable, invalid, or usable. [Validate the Response](#171-validate-the-response) explains how the example distinguishes those outcomes. [Inventory Meaning](#26-inventory-meaning) explains why a recorded zero or an old quantity must not be presented as a guaranteed current total.

### 3.3 Acquisition Reports

Reporting is an optional extension for a plugin that can confirm that a character obtained an item. It does **not** buy, move, consume, or create an item, and it does not unlock a collectible in the game. It sends a claim to Exportaramus, which can retain it in `reportedOwnership.items` for consumers to inspect separately from game observations.

For example, suppose your plugin already observes the successful completion of an item purchase. It can report that acquisition after confirming the outcome and character. Exportaramus may later observe the item in storage through its own readers, but that observation answers a different question: where the item was found. A report preserves the submitted acquisition claim even if the item is later consumed or moved. Reporting is not required for normal collection capture and must never be used to manufacture missing observations.

There are two names for the same reporting operation at different layers:

| Name | Layer | When to Use It |
| --- | --- | --- |
| `ReportOwnershipEvidence` | Exportaramus IPC endpoint, listed in [CollectionData Functions](#31-collectiondata-12). | Call this directly only if you implement your own connection and error handling. |
| `ReportConfirmedAcquisition` | Method on the supplied `CollectionDataIpcClient`. | Use this when your plugin uses the Consumer Kit. It checks the client state and character, calls the endpoint, and returns a result code. |

With the kit, add a helper like this inside your `Plugin` class in `Plugin.Dalamud.cs`. It uses the tutorial's existing `activeCharacter` and `collections` fields. Call it on a framework update only after your own code has confirmed an acquisition; the supplied tutorial does not contain a purchase detector or call this helper automatically.

```csharp
private string ReportAcquisition(string acquiredByContentId, uint baseItemId)
{
    if (activeCharacter is null || activeCharacter != acquiredByContentId)
        return "WrongCharacter";

    return collections.ReportConfirmedAcquisition(
        acquiredByContentId, baseItemId, "CollectionExample");
}
```

Keep the character ID associated with the confirmed event. Never substitute a newly logged-in character's ID when retrying an earlier report. `baseItemId` must be a recognized physical-item ID. The reporter label identifies the submitting plugin; it is trimmed, Unicode-normalized, limited to 64 UTF-8 bytes, and must contain no control characters. Capabilities list reporting availability, limits, and result codes; see [the capability schema](../data/Contracts/1.2/schemas/capabilities.schema.json).

The return value is a string code, not JSON. Use the [Acquisition Report Results](<Exportaramus - Backend Specification.md#134-acquisition-reports>) to decide whether to record acceptance, correct the request, wait, or stop reporting. `Accepted` means queued; it does not prove a game unlock or completed disk write.

Do not report planned purchases, a button click without a confirmed result, or ownership inferred from missing data. Reports establish neither permanent unlocks nor continued possession. Reporter labels are unauthenticated text, so consumers must not mistake them for proof supplied by the game. Inventory has no reporting endpoint; its quantities continue to come from observations.

### 3.4 Contract Versions and Schema Files

This guide uses CollectionData 1.2 and Inventory 1.0. A contract version identifies data definitions and endpoint names; it is independent of the plugin release number. Older CollectionData contracts have separate endpoints. Do not switch to one silently when a 1.2 call fails.

The Backend Specification contains the [contract prefixes](<Exportaramus - Backend Specification.md#131-versions-and-functions>), [all embedded JSON schemas](<Exportaramus - Backend Specification.md#21-json-schema-reference>), and [Inventory's field definitions](<Exportaramus - Backend Specification.md#14-inventory-ipc>). The schemas also live in [`data/Contracts/`](../data/Contracts/), and their C# types live in [`src/Contracts/`](../src/Contracts/). Use matching types, schemas, and prefixes.

Inventory does not advertise CollectionData's schema hash or provider session identity. Its capability shape and ordering limitations are defined separately in the backend; do not apply a CollectionData schema to Inventory JSON.

### 3.5 Saved Snapshots

An external application can read a saved Expanded Snapshot instead of using live IPC. This document adds names and ownership explanations but has a different structure from compact IPC JSON; do not deserialize it as an IPC snapshot. The expanded 1.2 document, `current-snapshot-v1.2.json`, includes names and catalog metadata alongside observations for its included collections. It is a file format, not a catalog IPC endpoint. See [Backend Specification, Section 15](<Exportaramus - Backend Specification.md#15-expanded-snapshot-files>) and the [complete expanded 1.2 schema](../data/Contracts/1.2/schemas/exportaramus-snapshot.schema.json).

Check the catalog language and game-data version, included collections, and explicit omission reasons. A collection's absence does not mean it has no entries. Appearance-storage entries cover observed contents; the Glamour Dresser does not provide a complete catalog of every appearance item. Full file publication can also lag lighter IPC updates, so use the file's own timestamps.

The kit retains the existing API names: `CollectionDataSemanticValidator.ValidateRich` validates Expanded Snapshot JSON and returns a result containing `RichSnapshotDocument`. Its `Root` property is a `JsonElement`; the kit does not provide a typed C# property for every expanded-document field. Read those fields according to the linked schema.

IPC serves a plugin running inside the game. A saved file lets an external application read a previously published document without a live IPC connection. It can remain on disk after logout or shutdown, so its character and timestamps describe that saved observation. The [Backend Storage specification](<Exportaramus - Backend Specification.md#12-local-storage>) explains the published files and their location.

Read and validate a complete replacement before updating the application's display. Limit an Expanded Snapshot to 16 MiB while reading, not just from the initial reported file length: the bound prevents an unexpectedly large file from consuming unbounded memory and parsing time.

Exportaramus replaces published files atomically, but an external reader can still encounter a temporary access or sharing failure while opening a replacement. File-change notifications can also arrive more than once for one update. Treat a notification as a reason to schedule a read, not proof that the document is new or valid. Combine repeated notifications and use a small, finite retry schedule for transient access failures. Stop retrying when that budget is exhausted; report the failure and wait for a later change or explicit retry.

Malformed, oversized, or unsupported JSON is not repaired by repeatedly reading the same bytes. Reject that replacement and wait for a new publication. A read that becomes obsolete because a newer update arrived must not replace a newer accepted document.

If a replacement fails, the previous valid file view may remain with its original age and a historical label. It must not be presented as a new observation or proof of the currently logged-in character.

### 3.6 FFXIV Collect Exports

FFXIV Collect JSON is a separate website-import format prepared from accepted data and reviewed mappings. It is not the CollectionData or Inventory response, and it does not merge earlier website exports.

| Exported Content | Website Effect |
| :--- | :--- |
| Collection key with IDs | Replaces that collection's progress. |
| Collection key with an empty array | Clears that collection's progress. |
| Omitted collection key | Leaves that collection unchanged. |

See [FFXIV Collect Export in the Backend Specification](<Exportaramus - Backend Specification.md#16-ffxiv-collect-export>) for mapping requirements, eligible observations, omissions, and output limits. The [README](../README.md#export-to-ffxiv-collect) explains the player import workflow.

### 3.7 Client and Snapshot Objects

The tutorial reads `collections.View`, then the accepted `View.Snapshot`, then the relevant collection or source. These are C# properties inside your plugin. They are not separate IPC functions.

Use the Backend Specification's [Client View](<Exportaramus - Backend Specification.md#136-consumer-kit-client-api>) for availability, failure, and lifetime behavior; [Typed Collection Values](<Exportaramus - Backend Specification.md#137-typed-collection-values>) for every property; and [Validation API](<Exportaramus - Backend Specification.md#138-validation-api>) for rejection reasons. JSON names use camelCase; the corresponding C# properties use PascalCase.

### 3.8 Source Metadata

A source's capability tells you whether Exportaramus has supported code to read it. Source health tells you whether there is a usable observation for this character. These are separate checks: an available reader can still have no observation.

Select both records with the same source key, such as `mounts` or `inventory`, after checking the key exists. The Backend Specification defines [source-health meanings](<Exportaramus - Backend Specification.md#42-source-health>), [all typed properties](<Exportaramus - Backend Specification.md#137-typed-collection-values>), and [allowed JSON values](<Exportaramus - Backend Specification.md#2131-common-definitions>). [Collection Meaning](#25-collection-meaning) shows how to use those definitions when displaying unlocked, locked, and unknown entries.

