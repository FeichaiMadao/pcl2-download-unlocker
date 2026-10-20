@echo off
setlocal
rem Windows + .NET Framework 4.8 (bundled csc.exe). No Visual Studio required.
set OUT=dist
if not exist "%OUT%" mkdir "%OUT%"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [error] csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)

"%CSC%" /nologo /target:exe /optimize+ /platform:anycpu ^
  /out:"%OUT%\PclUnlockPatcher.exe" ^
  /resource:"lib\Mono.Cecil.dll",Mono.Cecil.dll ^
  /r:"lib\Mono.Cecil.dll" ^
  "src\PclUnlockPatcher.cs"
if errorlevel 1 exit /b 1

echo Built %OUT%\PclUnlockPatcher.exe
