# Builds UnderspaceAccess and copies it into the game. Refuses while the game runs.
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'src\UnderspaceAccess'
$game = 'C:\program files (x86)\steam\steamapps\common\Underspace'
if (Get-Process Underspace -ErrorAction SilentlyContinue) { Write-Host 'Game is running: close it first.'; exit 1 }
dotnet build $proj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit 1 }
$data = Join-Path $game 'UserData\UnderspaceAccess'
New-Item -ItemType Directory -Force $data | Out-Null
Copy-Item (Join-Path $proj 'bin\Release\net6.0\UnderspaceAccess.dll') (Join-Path $game 'Mods') -Force
Copy-Item (Join-Path $PSScriptRoot 'lib\prism\prism.dll') $data -Force
Copy-Item (Join-Path $PSScriptRoot 'lib\prism\third-party') $data -Recurse -Force
Write-Host "Copied UnderspaceAccess.dll to Mods, prism.dll + licence to UserData\UnderspaceAccess"
