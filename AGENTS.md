# Notes for AI coding agents

Read this before changing anything. Humans: see [CONTRIBUTING.md](CONTRIBUTING.md).

## What this is
A Windows live wallpaper, desktop overlay and screensaver that draws an ocean of ASCII letters (sharks, a manta, a jellyfish, six fish species). Windows only.

## Layout
- `AsciiSaver.cs`: the shared core, one big file. `Engine` (letters, tiles), `Source` classes (`DesktopSource`, `ImageSource`, `AnimSource`, `OceanSource`), the screensaver `Program` (`/s /c /p /w /test`), settings dialog and window. Compiled into **three** things (see `build.ps1`).
- `OceanApp.cs`: `AsciiScreen.exe`, a native Win32 app (window, tray, hotkeys, overlay, live wallpaper). Entry point `AsciiSaver.OceanApp` (selected with `/main:`).
- `AppSettings.cs`: `AppConfig` (saved settings, `%APPDATA%\AsciiScreen\settings.ini`) and a plain Win32 fallback settings window.
- `settings-ui/`: WPF (.NET 10, Fluent theme) settings window. It **links `..\AsciiSaver.cs`** for the live preview, and writes the same ini file the running app reloads.
- `build.ps1`, `install.ps1`, `make-icon.ps1`, `app.manifest`, `app.ico`.

## Build and verify
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1                # must end with 0 Error(s) three times
cmd /c ".\AsciiSaver.scr /test out_a.png source=ocean w=1920 h=1080 font=8 frames=300"   # writes out_a.png and out_a.png.txt (ms/frame)
```
Always run `.scr` test renders through `cmd /c`. From PowerShell a `.scr` is started by the shell association, which forces `/S` (full-screen saver) and ignores your arguments. Look at the PNGs you produce; do not claim a visual change works without viewing a render. Delete your `out_*` files afterwards.

## Constraints (CI and reviewers enforce these)
- `AsciiSaver.cs` is compiled by the .NET Framework 4 `csc.exe` (C# 5): no `?.`, `$""`, `=>` members, `out var`, local functions, `nameof`. Test by running `build.ps1`.
- Keep the public surface other files use: `OceanOptions`, `Pointer`, `OceanSource`, `AnimSource`, `Engine`, `Grid`.
- Performance budget: under about 10 ms/frame at 1920x1080 and 20 ms/frame at 3840x1080 (`/test` output).
- Water stays plain black (no caustics or textures); far creatures are smaller, fainter and slower; bright body cells must stay above the tile threshold (about 0.355 before contrast) so they remain tiles.
- No network access, telemetry, or logging of user input. Do not add persistence beyond the settings file. Autostart stays opt-in.
- Do not commit build output or renders. Do not touch the registry, `%APPDATA%`, or the installed copy from scripts or tests.

## Gotchas
- Launching `AsciiScreen.exe /wallpaper` reparents its window into Explorer's desktop window; it is layered (`SetLayeredWindowAttributes`) because plain GDI painting gets an empty visible region there on Windows 11 24H2+.
- Stopping the app by name: a process started directly shows as `AsciiScreen`; a `.scr` started by the shell can show as `AsciiSaver.scr`.
- Test hooks (env vars): `ASCIISCREEN_SHOT`, `ASCIISCREEN_SHOT_DELAY`, `ASCIISCREEN_LOG`, `ASCISAVER_DUMP`.

## Pull requests
Small and focused, with the render and ms/frame attached. Say if the change was AI-assisted (optional). A human maintainer must approve before merge into `main`.
