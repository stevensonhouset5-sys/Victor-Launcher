# Setup Notes

## 1. Open in Rider

Open:

`/Users/tatestevenson-house/Documents/Codex/2026-04-19-are-you-able-to-code-among/AmongUsModStarter.sln`

## 2. Install the .NET SDK

Right now `dotnet` is missing in the terminal, so build/restore will fail until you install the SDK.

After installing it, reopen Rider or reload the solution.

## 3. Plugin project

Files:

- `AmongUsPlugin/AmongUsPlugin.csproj`
- `AmongUsPlugin/PluginInfo.cs`
- `AmongUsPlugin/StarterPlugin.cs`

This project becomes your mod DLL.

Build output target:

`AmongUsPlugin/bin/Debug/net6.0/StarterAmongUsPlugin.dll`

Put that DLL into:

`<Among Us folder>/BepInEx/plugins/`

## 4. Launcher project

Files:

- `LauncherCore/LauncherCore.csproj`
- `LauncherCore/ModManifest.cs`
- `LauncherCore/InstallPlanner.cs`

This is not the full launcher UI yet. It is the safe core we can build on next:

- manifest model
- dependency/conflict checks
- install destination planning

## 5. What to do first

Your first practical checkpoint is:

1. Install `.NET SDK`
2. Open the solution in Rider
3. Restore packages
4. Build `AmongUsPlugin`
5. Copy the DLL into `BepInEx/plugins`
6. Launch the game and check `BepInEx/LogOutput.log`

If the plugin loads, we can move to the next step:

- press `F7` in game to open the mod manager
- install a DLL by pasting its full path
- enable or disable DLLs for the next launch
