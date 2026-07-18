# ScarletTeleportGUI

ScarletTeleportGUI is a client-side V Rising mod that adds an in-game STP panel.

It follows the same client bootstrap and chat interception pattern as FamBook, uses `.stp ltp` to fetch teleport data, and provides Player teleport actions in the same panel.

## Current Behavior

- Adds an `STP` button to the HUD.
- Opens a custom panel with `Private`, `Public`, `Waypoints`, and `Player` tabs.
- Sends `.stp ltp` when the panel opens.
- Intercepts `System` chat messages while the panel is open and the teleport list is pending.
- Parses teleport output into public/private/player/waypoint collections and hides consumed lines from chat.
- Player tab supports:
	- `.stp tpr [name]` via `Teleport`
	- `.stp tpa [name]` via `Accept`



## Build

```powershell
dotnet restore
dotnet build -c Release
```

Output DLL:

- `bin/Release/net6.0/ScarletTeleportGUI.dll`