# Victor Launcher

Victor Launcher is an in-game BepInEx plugin for `Among Us` that helps players manage DLL-based mod packs without manually dragging files around every time.

It includes:

- an in-game `F7` launcher menu
- DLL install, enable, disable, and delete controls
- room-code downloads for shared mod packs
- folder-based pack grouping inside the menu
- a matching browser-based room manager for hosts

## What This Repo Contains

- [AmongUsPlugin](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin): the in-game Victor Launcher plugin
- [room-admin-site/index.html](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/room-admin-site/index.html): the single-file room manager website
- [supabase](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase): SQL, storage policy, and Edge Function files for the backend

## Current Plugin Build

The current plugin output is:

- [Victor-Launcher-0.1.2.dll](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/bin/Debug/net6.0/Victor-Launcher-0.1.2.dll)

Victor Launcher shows up in BepInEx as:

- `Victor Launcher`

## Installing The Plugin

1. Build the project or use the latest DLL from the build output.
2. Copy the DLL into your game install here:

`<Among Us folder>/BepInEx/plugins/`

3. Launch the game.
4. Press `F7` to open Victor Launcher.

Victor Launcher manages:

- enabled plugins from `BepInEx/plugins`
- disabled plugins from `BepInEx/plugins-disabled`
- queued imports from `BepInEx/mod-imports`

## Room-Code Downloads

Hosts can create a room, upload DLLs, save a room pack, and share the room code.

Players then:

1. open Victor Launcher in game
2. enter the room code
3. download the room pack into the queue
4. install the queued DLLs
5. restart the game when prompted

## Website

The host website is a single self-contained HTML file:

- [room-admin-site/index.html](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/room-admin-site/index.html)

It is already wired for your Supabase project and no longer exposes editable project URL or anon key fields in the UI.

Hosts can:

- create a room
- open an existing room
- upload DLLs
- remove DLLs
- save the room pack
- copy the room code

## Supabase Backend

Supabase setup files live here:

- [room_schema.sql](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/room_schema.sql)
- [storage_policies.sql](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/storage_policies.sql)
- [DEPLOYMENT.md](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/DEPLOYMENT.md)

Edge Functions live under:

- [/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/functions](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/supabase/functions)

## Before Publishing

Good last checks before you push to GitHub:

- confirm the website file is the polished version in [room-admin-site/index.html](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/room-admin-site/index.html)
- confirm the plugin still opens with `F7`
- confirm the latest room-code flow works in game
- decide whether you want to keep your current public Supabase project URL and anon key hardcoded in the repo
- avoid committing private signing keys or service-role secrets

Important note:

- the Supabase anon key is designed to be public, but your service-role key, room session secret, and manifest signing private key should never be committed

## Development

Open:

- [AmongUsModStarter.sln](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsModStarter.sln)

Main plugin files:

- [StarterPlugin.cs](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/StarterPlugin.cs)
- [ModManagerBehaviour.cs](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/ModManagerBehaviour.cs)
- [SupabasePackService.cs](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/SupabasePackService.cs)
- [ModFileService.cs](/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/ModFileService.cs)

Build with:

```bash
dotnet build /Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsPlugin/AmongUsPlugin.csproj
```
