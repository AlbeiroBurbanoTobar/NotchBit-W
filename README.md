# NotchBit-W

Windows version of [BitNotch](https://github.com/AlbeiroBurbanoTobar/BitNotch): a floating "island" at the top center of the
screen that will show what is playing and keep a shelf of files.

Status: **phase 3** — the island, the module system and music. The real shelf comes next.

## What works

- At rest, a thin white line at the top center of the primary screen (like the gesture bar on a
  phone). Always on top, no frame, not in the taskbar or Alt+Tab, and it never takes focus from
  the app you are using.
- Hovering near it grows the line into a black island with an animation; moving away closes it
  after a short delay. At rest the window is only a little bigger than the line, so it does not
  block clicks around it.
- It hides while an app covers the screen (a fullscreen video or game, or a presentation) and
  comes back when you leave it.
- It stays centered on the primary monitor when monitors are added, removed, or rescaled, using
  that monitor's own display scale.
- A notification-area icon; right-click it and choose **Salir de NotchBit** to quit.
- Modules: the open island shows a tab per enabled module and the selected module's page.
  Dragging files over the island opens it and hands them to the first module that accepts file
  drops, switching to its tab. Which modules are turned off is kept in
  `%LOCALAPPDATA%\NotchBit\settings.json` (`DisabledModules`); there is no settings window yet.
  The Shelf tab is a placeholder for now.
- **Music**: shows the current track, artist and artwork from whatever Windows reports as Now
  Playing (Spotify, browsers, Apple Music, ...), with play/pause, previous, next and a seekable
  progress bar. Previous, next and seeking are disabled where the player does not support them.
  While something plays, the resting line widens into a small black pill with the artwork on the
  left and animated bars on the right; the bars take the dominant color of the artwork and are
  decorative. It goes back to the line a moment after playback stops.

## Requirements

- Windows 10 version 1809 or later (developed on Windows 11)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)

No Visual Studio is needed: the Windows App SDK comes in as a NuGet package.

## Building and running

```bash
dotnet build src/NotchBit/NotchBit.csproj -p:Platform=x64
```

```bash
src/NotchBit/bin/x64/Debug/net10.0-windows10.0.19041.0/NotchBit.exe
```

Use `-p:Platform=ARM64` on ARM devices.

## Layout

| Folder | Contents |
|---|---|
| `src/NotchBit/App` | Tray icon |
| `src/NotchBit/Core` | `IIslandModule`, `ModuleRegistry`, JSON `SettingsStore` |
| `src/NotchBit/Modules/Music` | Now Playing provider (GSMTC), controller, player and compact views, artwork color |
| `src/NotchBit/Modules` | The other modules (the shelf is a placeholder until phase 4) |
| `src/NotchBit/Island` | Island window, its animation, the tabs, and the Win32 calls WinUI does not wrap |
| `src/NotchBit/Assets` | App icon |

## How the window is made frameless

WinUI 3 has no fully frameless, transparent window out of the box. The island combines:

- `WinUIEx.TransparentTintBackdrop` for a transparent background, so only the pill is drawn;
- an `OverlappedPresenter` without border or title bar, always on top;
- `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` to stay out of Alt+Tab and never steal focus;
- a window subclass that answers `WM_NCCALCSIZE` with no non-client area, because the presenter
  keeps putting back a 3px dialog frame;
- `DWMWA_WINDOW_CORNER_PREFERENCE` and `DWMWA_BORDER_COLOR` to drop Windows 11's own rounding and border.

Music uses the Windows System Media Transport Controls (`GlobalSystemMediaTransportControlsSessionManager`),
so it needs no permissions and no per-player code. Players report their position rarely, so the
controller estimates it between readings, and keeps a command's expected effect for a couple of
seconds so the play button does not flicker back on a stale reading.

Fullscreen detection uses `SHQueryUserNotificationState`, the signal Windows itself uses to hold
back notifications, polled twice a second. The same window subclass listens for `WM_DISPLAYCHANGE`
and `WM_DPICHANGED` to re-center the island.
