# Builds AsciiSaver.scr and AsciiScreen.exe with the C# compiler that ships with Windows (no SDK needed).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
$refs = '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll'

# 1) the screen saver
& $csc /nologo /target:winexe /optimize+ /platform:x64 @refs `
    /win32icon:"$here\app.ico" /main:AsciiSaver.Program /out:"$here\AsciiSaver.scr" "$here\AsciiSaver.cs"
if ($LASTEXITCODE -ne 0) { throw "Screen saver compile failed" }
Write-Host "Built $here\AsciiSaver.scr"

# 2) the native Win32 desktop app (shares the renderer and ocean with the saver)
& $csc /nologo /target:winexe /optimize+ /platform:x64 @refs `
    /win32manifest:"$here\app.manifest" /win32icon:"$here\app.ico" /main:AsciiSaver.OceanApp /out:"$here\AsciiScreen.exe" "$here\AsciiSaver.cs" "$here\OceanApp.cs" "$here\AppSettings.cs"
if ($LASTEXITCODE -ne 0) { throw "AsciiScreen compile failed" }
Write-Host "Built $here\AsciiScreen.exe"



# 3) the Windows 11 (Fluent) settings app - needs the .NET SDK; AsciiScreen falls back to a built-in window without it
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet build "$here\settings-ui" -c Release -o "$here\settings-app" --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Settings app build failed" }
    Write-Host "Built $here\settings-app\AsciiScreenSettings.exe"
} else {
    Write-Host "dotnet not found: skipped the Fluent settings app"
}

