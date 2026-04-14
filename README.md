# FamBook

FamBook is a client-side V Rising mod that adds an in-game familiar browser panel.

It sends familiar commands, intercepts the server System chat responses, parses familiar data, and renders a paged UI so you can browse and bind familiars without chat spam.

## What It Does

- Adds a FamBook button to the HUD.
- Opens a custom panel that shows up to 10 familiars per page.
- Navigates familiar boxes/pages with previous and next controls.
- Requests familiar data by sending:
  - `.fam cb boxN`
  - `.fam l`
- Intercepts and parses familiar list messages from System chat.
- Supports bind actions from the panel (`.fam b N`) and auto-retry with unbind when needed.
- Hides intercepted familiar listing messages from normal chat output.

## Requirements

### Runtime

- Windows client install of V Rising.
- BepInEx 6 (IL2CPP) installed for V Rising.
- A server/mod setup that supports the familiar commands used by FamBook (for example Bloodcraft `.fam` commands).

### Build

- .NET 6 SDK.
- NuGet access to:
  - https://api.nuget.org/v3/index.json
  - https://nuget.bepinex.dev/v3/index.json

The project targets net6.0 and references:

- BepInEx.Unity.IL2CPP
- BepInEx.PluginInfoProps
- VampireReferenceAssemblies

## Build Instructions

From the project directory:

```powershell
dotnet restore
dotnet build -c Release
```

Output DLL:

- `bin/Release/net6.0/FamBook.dll`

## Install

1. Copy `FamBook.dll` to your V Rising plugins folder:
   - `.../VRising/BepInEx/plugins`
2. Start the game.
3. Confirm plugin load in the BepInEx log.

Note:

- The plugin is client-only and skips patching on dedicated server builds.

## Build Auto-Copy Behavior

The project includes a post-build target that attempts to auto-copy `FamBook.dll` after build to:

- `C:\Program Files (x86)\Steam\steamapps\common\VRising\BepInEx\plugins`
- `D:\Steam\steamapps\common\VRising\BepInEx\plugins`

If your game path is different, either:

- Update the copy target paths in `FamBook.csproj`, or
- Copy the DLL manually from `bin/Release/net6.0/`.

## Configuration

Config file:

- `BepInEx/config/com.fambook.vrising.cfg`

Options:

- `UIOptions.FamiliarsPanel` (default: true)
  - Enables or disables the FamBook panel.
- `UIOptions.ShowActiveIndicator` (default: true)
  - Highlights the active familiar in the panel.
- `UIOptions.Eclipsed` (default: true)
  - Uses faster update intervals. Set false for lower update frequency if needed.

## Development Notes

- Harmony patches initialize when game data and UI canvas are ready.
- State resets cleanly on client bootstrap destroy (disconnect/scene exit).
- Familiar parsing is regex-based for Bloodcraft-style System message formats.

## Troubleshooting

- No panel appears:
  - Verify BepInEx is installed and plugin loaded.
  - Check `UIOptions.FamiliarsPanel=true`.
- No familiar data appears:
  - Confirm the server supports `.fam cb` and `.fam l` commands.
  - Check logs for intercepted message lines and parse errors.
- Build succeeds but no DLL in game:
  - Auto-copy path likely does not match your Steam library location.
  - Copy `bin/Release/net6.0/FamBook.dll` manually.
