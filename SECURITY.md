# Security policy

## Reporting a vulnerability

Please **do not** open a public issue for a security problem. Use GitHub's private reporting: on the repository page choose **Security**, then **Report a vulnerability**. Include what you found, how to reproduce it, and the Windows version.

I will acknowledge a report as soon as I can and aim to fix real problems quickly.

## Scope and supported versions

Only the latest `main` is supported. The project has no server component and makes no network connections. Relevant areas: the global hotkeys and system-wide cursor and mouse-button polling used by the overlay and wallpaper modes, the opt-in "Start with Windows" `Run` registry value, the settings file in `%APPDATA%\AsciiScreen`, and the scripts (`build.ps1`, `install.ps1`).

Release binaries are not published yet. Build from source and review the code you run.
