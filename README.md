# Design Bulk Editor

[![Build](https://github.com/STRNDV/Design-Bulk-Editor/actions/workflows/build.yml/badge.svg)](https://github.com/STRNDV/Design-Bulk-Editor/actions/workflows/build.yml)

A Dalamud plugin for bulk-editing your [Glamourer](https://github.com/Ottermandias/Glamourer)
design library - rename designs, reorganise folders, and change a property
across many designs at once - with a live preview on your logged-in
character before you save anything to disk.

> **This project is unofficial and not affiliated with Glamourer or its
> author (Ottermandias).** It is a third-party tool that reads and writes
> Glamourer's own design files and talks to its public IPC.
>
> **This project was built with heavy AI assistance.** Please read
> [AI_DISCLOSURE.md](AI_DISCLOSURE.md) before you trust it with your design
> library or load it into your game.

## Why this exists

An earlier, personal version of this tool (a standalone WPF app) worked, but
its UX broke down after a single edit pass: it cached a "reference design"
for its bulk-edit dropdowns, forced a save→reload cycle after every change,
and threw away half a batch if one design failed to save. This rewrite fixes
that by design, not by patching around it - see
["How the UX problem is actually fixed"](#how-the-ux-problem-is-actually-fixed)
below.

## What it does (v1)

- Loads your Glamourer `designs` folder (with a folder-browse button) and
  groups designs by character
- Groups designs by character even if you don't use the "(Character) Design"
  naming convention - see [Character assignment](#character-assignment)
- Lets you rename designs, change their folder, and edit any
  Customize/Equipment/Parameters property, individually or across many
  designs at once, with a fitting editor per value (checkbox, number field,
  color picker, or text) instead of always a raw text box
- Batch-renames character/design names across every targeted design with a
  find-and-replace rule
- Stages any number of property edits at the same time, freely switchable,
  with nothing discarded when you change your mind or move to a different
  property
- Shows a diff - old value vs. new value, per design - before anything is
  written to disk, so a bulk save is never a leap of faith
- Previews a staged, *not-yet-saved* edit live on your logged-in character
  via Glamourer's own IPC, before you commit anything to disk
- Backs up every design file automatically before it gets overwritten,
  writes atomically so a crash mid-save can never leave a half-written
  file, and lets you browse and one-click restore any previous backup of a
  design (the restore itself is backed up too, so it's undoable)
- Exports a design as a shareable Glamourer code, and imports a shared code
  as a new design, using Glamourer's own encoder/decoder via its IPC
- Never aborts a batch save partway through: if one design fails, the rest
  still get processed, and you get a clear per-design error report

### Explicitly out of scope for v1

- Editing anyone other than your own logged-in character for live preview
  (no NPCs, no other players)
- Submitting to the official Dalamud plugin repository - for now this is a
  dev-plugin / self-hosted repo install only

## How the UX problem is actually fixed

| | Old app | This plugin |
|---|---|---|
| Property selection | 3 cascading dropdowns backed by one cached "reference design" | Recomputed fresh from the focused design on every single frame - there is no cache to go stale |
| Editing several properties | One value field; switching to a new property discarded the previous one | A persistent list of staged edits; add, remove, or switch between as many as you like |
| After saving | A modal told you to reload manually; state was inconsistent until you did | Nothing to reload - state lives in memory for as long as the plugin/game session runs |
| A batch with one bad design | Threw and aborted mid-batch, leaving a mix of saved/unsaved designs | Every design in a batch is attempted; failures are reported per-design |
| Feedback | None until you saved and reloaded | Live preview on your character via Glamourer's IPC, before saving anything |
| Which designs get edited | Not applicable (single design at a time) | One selection: a checkbox adds a design without disturbing the rest, clicking a name selects just that one, Ctrl+click adds/removes - every action (stage, apply, save, discard) always acts on exactly this selection, so a design you edited earlier can never silently drop out of it |
| "Did I actually save that?" | Not applicable | A library-wide summary always shows how many designs have unsaved edits, independent of what's currently selected, with a one-click way to select exactly those |

Note the distinction between **selecting** designs (what gets edited/saved) and the
**primary** design (whichever you selected most recently) - the primary is only
used to browse which Section/Entry/Property options are available to stage,
and for the inherently single-design actions (rename, character assignment,
backups, share code). Staging, applying, reviewing, saving, and batch rename
all act on the *whole selection*, not just the primary.

## Character assignment

Glamourer's own design files have no "this belongs to character X" field -
designs are deliberately character-agnostic templates. So this plugin
doesn't assume everyone names designs `(Character) Design Name`. Instead, it
suggests an assignment from whichever signal is available, in this priority
order, and always lets you confirm or correct it:

1. **Glamourer's own Automation config** (`automation.json`), if you use
   Automation Sets - the most authoritative signal, since it's something you
   explicitly configured in Glamourer itself. This file's format is
   undocumented and can change between Glamourer versions, so parsing it is
   defensive: any surprise is treated as "no data", never an error.
2. **The `(Character) Design Name` prefix convention**, if your design's
   name follows it.
3. **The top-level folder** in the design's `FileSystemFolder`, if you
   organise your library into per-character folders.
4. Otherwise the design is left unassigned ("Unassigned" group) until you
   assign it yourself.

Whatever you confirm or correct is remembered in a small file the plugin
owns (`character-assignments.json` in the plugin's own config directory) and
always takes priority over the heuristics above from then on.

## Project layout

```
src/
  DesignBulkEditor.Core/    Plain .NET library: design model, file I/O with
                               backup + atomic writes, character-assignment
                               logic. No Dalamud or ImGui dependency - fully
                               unit-testable without the game.
  DesignBulkEditor.Plugin/  The Dalamud plugin itself: ImGui UI and the
                               Glamourer.Api integration.
tests/
  DesignBulkEditor.Core.Tests/   xUnit tests for everything in Core.
```

## Glamourer.Api integration

Live preview is built on the official
[`Glamourer.Api`](https://www.nuget.org/packages/Glamourer.Api) NuGet
package (MIT-licensed, maintained alongside Glamourer itself), not on
hand-written or guessed IPC calls:

- `IGlamourerApiBase.ApiVersion` is checked on load; if Glamourer isn't
  running or the handshake fails for any reason, live preview is disabled
  and clearly reported, but offline file editing keeps working.
- `IGlamourerApiState.ApplyState` pushes a staged, unsaved edit onto your
  character with `ApplyFlag.Once`, so a preview is never written into
  Glamourer's own automation state.
- `IGlamourerApiState.RevertState` reverts the preview back to your normal
  state.
- `IGlamourerApiDesigns.GetDesignBase64` / `AddDesign` power the share-code
  export/import, so encoding stays byte-for-byte compatible with Glamourer
  itself instead of us reverse-engineering its (undocumented, binary) code
  format.
- Saving an edited design to disk is plain file I/O - there is no
  `UpdateDesign` call in Glamourer's IPC, so committing a change has always
  meant writing the JSON file directly, both in the old app and this one.

## Building

Requires the .NET 10 SDK and a local Dalamud install (via XIVLauncher) for
the Plugin project; `Dalamud.NET.Sdk` resolves it automatically from the
standard XIVLauncher addon path.

```bash
dotnet build src/DesignBulkEditor.Core/DesignBulkEditor.Core.csproj
dotnet test tests/DesignBulkEditor.Core.Tests/DesignBulkEditor.Core.Tests.csproj

# Build the Plugin through the solution (or with -p:Platform=x64), not the
# bare .csproj alone: the solution pins the Plugin project to the x64
# platform Dalamud actually loads, which changes the output folder below.
dotnet build DesignBulkEditor.slnx
```

CI only builds and tests `Core` (see [`.github/workflows/build.yml`](.github/workflows/build.yml))
since a clean CI runner has no local Dalamud install to compile the Plugin
project against. The Plugin is verified manually - see below.

## Installing

There are two different, unrelated ways to install this plugin - pick one:

### As a regular user, via the custom plugin repository

This is the normal install path, the same mechanism Penumbra/Glamourer/etc.
use: a `repo.json` file hosted in this repo, that you point Dalamud at.

1. In-game, open the Dalamud settings (`/xlsettings`) → Experimental →
   Custom Plugin Repositories, and add:
   `https://raw.githubusercontent.com/STRNDV/Design-Bulk-Editor/master/repo.json`
2. Install "Design Bulk Editor" from the plugin installer like any other
   plugin, and update it the same way too.

`repo.json` lives in this repo and points at the `latest.zip` asset of this
repo's most recent GitHub Release (built by
[`.github/workflows/release.yml`](.github/workflows/release.yml) whenever a
`v*` tag is pushed) - it is a small, separate JSON file, not the build
output described below, and it's the only thing that needs a public URL at
all.

### As a developer, via a local dev-plugin build

For working on the plugin itself. Every build already produces a loose,
loadable plugin manifest next to the DLL on your own machine - nothing here
is uploaded anywhere.

1. Build via the solution as shown above (Debug is fine for local testing).
   This produces `DesignBulkEditor.json` under
   `src/DesignBulkEditor.Plugin/bin/x64/Debug/`.
2. In-game, open the Dalamud settings (`/xlsettings`) → Experimental → add
   that file's path under Dev Plugin Locations.
3. Enable it from the plugin installer's "Dev Tools" tab.
4. Open it with `/designbulk`.

## Releasing

1. Bump `<Version>` in
   [`src/DesignBulkEditor.Plugin/DesignBulkEditor.Plugin.csproj`](src/DesignBulkEditor.Plugin/DesignBulkEditor.Plugin.csproj)
   and `AssemblyVersion` in [`repo.json`](repo.json) to match (Dalamud
   compares this to decide whether an update is available - the two must
   agree).
2. Commit, then push a tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. The release workflow builds against a freshly installed Dalamud and
   publishes `latest.zip` to a GitHub Release matching the tag.
   `repo.json`'s download links always point at the latest release, so they
   don't need to change.

## Status

Early, hand-built v1. The Core library has full unit test coverage; the
Plugin UI has been verified to build against a real, locally installed
Glamourer 1.7.1.3 / Dalamud API level 15, but still needs an in-game smoke
test pass (load a real design folder, stage edits across sections, preview,
save, confirm backups) before being called stable.
