# Build Floating Dock with the Windows built-in .NET Framework compiler (no SDK needed).
param([switch]$Run)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'FloatingDock.exe'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $net 'WPF'
$csc = Join-Path $net 'csc.exe'

if (-not (Test-Path $csc)) { throw "C# compiler not found: $csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# the output exe is locked while running
Get-Process FloatingDock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$refs = @(
    (Join-Path $net 'System.dll'),
    (Join-Path $net 'System.Core.dll'),
    (Join-Path $net 'System.Runtime.Serialization.dll'),
    (Join-Path $net 'System.Xaml.dll'),
    (Join-Path $net 'System.Drawing.dll'),
    (Join-Path $net 'System.Windows.Forms.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll')
) | ForEach-Object { "-r:$_" }

$files = Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName }

$argsList = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/debug-', '/codepage:65001',
              "/out:$out", "/win32manifest:$(Join-Path $src 'app.manifest')") + $refs + $files

$icon = Join-Path $root 'assets\dock.ico'
if (Test-Path $icon) { $argsList += "/win32icon:$icon" }

& $csc $argsList
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

Write-Host ("OK -> {0} ({1} KB)" -f $out, [math]::Round((Get-Item $out).Length/1kb,1))

if ($Run) {
    Start-Process -FilePath $out
    Write-Host 'started'
}
