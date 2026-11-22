@echo off
rem Rebuilds "Dungeon Keys.exe" from src\Program.cs (uses the C# compiler built into Windows).
rem If src\icon.ico exists, it is used as the program's icon.
cd /d "%~dp0"
set ICON=
if exist "src\icon.ico" set ICON=/win32icon:"src\icon.ico"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ %ICON% ^
  /out:"Dungeon Keys.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\Program.cs
if errorlevel 1 (echo Build failed. & pause) else echo Build OK.
