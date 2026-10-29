# Copies the built AsciiScreen app to a permanent per-user folder and puts a shortcut on the Desktop.
# Run build.ps1 first. Safe to run again after a rebuild: it replaces the installed copy.
$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$target  = Join-Path $env:LOCALAPPDATA 'Programs\AsciiScreen'
$desktop = [Environment]::GetFolderPath('Desktop')

if (-not (Test-Path "$here\AsciiScreen.exe")) { throw "AsciiScreen.exe not found next to this script. Run build.ps1 first." }

# the exe can't be replaced while it is running
Get-Process -Name 'AsciiScreen', 'AsciiScreenSettings' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$here\AsciiScreen.exe" $target -Force
Copy-Item "$here\app.ico" $target -Force
if (Test-Path "$here\AsciiSaver.scr") { Copy-Item "$here\AsciiSaver.scr" $target -Force }   # the screen saver, kept next to the app
if (Test-Path "$here\settings-app") {
    New-Item -ItemType Directory -Force "$target\settings-app" | Out-Null
    Copy-Item "$here\settings-app\*" "$target\settings-app" -Recurse -Force
}

$lnkPath = Join-Path $desktop 'Ascii Screen.lnk'
$sh  = New-Object -ComObject WScript.Shell
$lnk = $sh.CreateShortcut($lnkPath)
$lnk.TargetPath       = Join-Path $target 'AsciiScreen.exe'
$lnk.Arguments        = '/wallpaper'
$lnk.WorkingDirectory = $target
$lnk.IconLocation     = (Join-Path $target 'AsciiScreen.exe') + ',0'
$lnk.Description      = 'Ascii Screen - a live ocean wallpaper (Ctrl+Alt+W toggles it, Ctrl+Alt+O is the click-through overlay)'
$lnk.Save()

Write-Host "Installed to $target"
Write-Host "Shortcut:   $lnkPath"
