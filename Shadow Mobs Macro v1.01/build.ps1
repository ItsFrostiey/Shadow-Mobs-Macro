# Compiles "Shadow Mobs Macro.exe". Needs nothing installed: the C# compiler ships with Windows.
# The logo and calibration examples in assets\ are built into the exe, so the exe plus data\jobs.txt
# is all another PC needs.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
$resources = Get-ChildItem (Join-Path $root 'assets') -Filter *.png -Recurse | ForEach-Object { "/res:$($_.FullName),$($_.Name)" }
$out = Join-Path $root 'Shadow Mobs Macro.exe'
& $csc /nologo /optimize+ /target:winexe "/win32icon:$(Join-Path $root 'icon.ico')" "/out:$out" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @resources @sources
if ($LASTEXITCODE -ne 0) { throw "Compile failed" }
Write-Host "Built $out"
