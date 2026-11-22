@echo off
rem Builds the program and creates Release\Dungeon Keys v<version>.zip
rem Keep VERSION in sync with the AssemblyVersion in src\Program.cs.
setlocal
set VERSION=1.0.0
cd /d "%~dp0"

call build.bat
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$stage = Join-Path 'Release' 'Dungeon Keys';" ^
  "if (Test-Path 'Release') { Remove-Item 'Release' -Recurse -Force };" ^
  "New-Item -ItemType Directory -Force (Join-Path $stage 'src') | Out-Null;" ^
  "Copy-Item 'Dungeon Keys.exe','README.txt','build.bat' $stage;" ^
  "Copy-Item 'src\Program.cs','src\icon.ico' (Join-Path $stage 'src');" ^
  "Compress-Archive -Path $stage -DestinationPath ('Release\Dungeon Keys v%VERSION%.zip') -CompressionLevel Optimal;" ^
  "Write-Host 'Created Release\Dungeon Keys v%VERSION%.zip'"
if errorlevel 1 (echo Release failed. & pause & exit /b 1)
