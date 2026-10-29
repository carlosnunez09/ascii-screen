# Contributing to Ascii Screen

Thanks for wanting to help. Bug reports, ideas, fixes, new creatures and polish are all welcome.

## AI-assisted contributions are welcome

Using Claude, Codex, Copilot or any other AI tool to write code, tests or docs is fine here. The rules are the same as for any contribution:

- **You are responsible for what you submit.** Read and understand every line, and run it. Do not submit output you have not checked.
- **Say so if you like.** There is an optional checkbox in the pull request template for AI assistance and which tool; it is for context, not a penalty.
- **Show evidence.** Include the render or measurement that proves the change works (see *Testing*).
- **Licensing:** only submit work you have the right to license under the MIT license. Do not paste code copied from other projects unless its license allows it, and credit it.
- No secrets, personal data, or machine-specific paths.
- AI coding agents should read [AGENTS.md](AGENTS.md) first.

## How changes get in

1. Fork the repo (or create a branch if you have access) and make a branch from `main`.
2. Make a focused change. Small pull requests are reviewed faster.
3. Open a pull request. The **build** check must pass.
4. The maintainer reviews and **approves** before anything is merged into `main`. `main` is protected: no direct pushes, no force-pushes.

## Building and testing

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build must finish with `0 Error(s)` for the screensaver, the app and the settings window.

To check how a change looks and performs without opening a window, render frames headlessly (run through `cmd`; launching a `.scr` straight from PowerShell runs it full-screen instead):

```powershell
cmd /c ".\AsciiSaver.scr /test out_a.png source=ocean w=1920 h=1080 font=8 frames=300"
```

This writes `out_a.png` and `out_a.png.txt` (cell grid size and **ms per frame**). Options: `source=` (`ocean`, `plasma`, `tunnel`, `fire`, `desktop`, `images`), `w=`, `h=`, `frames=`, `font=` (letter size in px), `style=`, `tail=N` (also save the last N frames as `out_a_0.png` ...), `px=`/`py=` (cursor position, 0 to 1), `ripple=1` (simulate a click), `clicks=0` (turn click ripples off). Please attach the image and the ms/frame to your pull request when you change how things look or move. Keep 1920x1080 under about 10 ms/frame and 3840x1080 under about 20 ms/frame.

## Code guidelines

- **`AsciiSaver.cs` must stay C# 5 compatible**: it is compiled by the .NET Framework 4 `csc.exe` as well as by the modern SDK (the settings window links the same file). No `?.`, no string interpolation, no expression-bodied members, no `out var`, no local functions, no `nameof`.
- Match the surrounding style and comment density. Comments only where the intent is not obvious.
- No dead code, no leftover debug output.
- Do not commit build output (`*.exe`, `*.scr`, `settings-app/`) or scratch renders.
- Keep the water plain black: the look relies on creatures standing out of the dark.

## Reporting bugs and ideas

Open an issue using the templates. For security problems see [SECURITY.md](SECURITY.md).

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
