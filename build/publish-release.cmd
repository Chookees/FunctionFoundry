@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Restore, build Release, pack packages, and collect deliverables into .\Release
cd /d "%~dp0\.."
set "ROOT=%CD%"
set "RELEASE_DIR=%ROOT%\Release"
set "STAGE_BIN=%RELEASE_DIR%\bin"
set "STAGE_PACKAGES=%RELEASE_DIR%\packages"

set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo ==> Repository root: %ROOT%
echo ==> Cleaning %RELEASE_DIR%
if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
mkdir "%STAGE_BIN%"
mkdir "%STAGE_PACKAGES%"

echo ==> Restoring
dotnet restore FunctionFoundry.slnx
if errorlevel 1 exit /b 1

echo ==> Building Release
dotnet build FunctionFoundry.slnx -c Release --no-restore
if errorlevel 1 exit /b 1

echo ==> Packing NuGet packages
if exist "%ROOT%\artifacts\packages" rmdir /s /q "%ROOT%\artifacts\packages"
mkdir "%ROOT%\artifacts\packages"
dotnet pack FunctionFoundry.slnx -c Release --no-build -o "%ROOT%\artifacts\packages"
if errorlevel 1 exit /b 1

echo ==> Collecting library binaries
for /d %%D in ("%ROOT%\src\FunctionFoundry.*") do (
  set "NAME=%%~nxD"
  set "SRC_OUT=%ROOT%\artifacts\bin\!NAME!\release"
  if exist "!SRC_OUT!" (
    mkdir "%STAGE_BIN%\!NAME!" >nul 2>&1
    if exist "!SRC_OUT!\!NAME!.dll" copy /y "!SRC_OUT!\!NAME!.dll" "%STAGE_BIN%\!NAME!\" >nul
    if exist "!SRC_OUT!\!NAME!.xml" copy /y "!SRC_OUT!\!NAME!.xml" "%STAGE_BIN%\!NAME!\" >nul
    if exist "!SRC_OUT!\!NAME!.pdb" copy /y "!SRC_OUT!\!NAME!.pdb" "%STAGE_BIN%\!NAME!\" >nul
    if exist "!SRC_OUT!\!NAME!.deps.json" copy /y "!SRC_OUT!\!NAME!.deps.json" "%STAGE_BIN%\!NAME!\" >nul
    echo     !NAME!
  ) else (
    echo     skip !NAME! ^(no release output^)
  )
)

echo ==> Collecting NuGet packages
set "PACKAGE_COUNT=0"
for %%F in ("%ROOT%\artifacts\packages\FunctionFoundry.*.nupkg" "%ROOT%\artifacts\packages\FunctionFoundry.*.snupkg") do (
  if exist "%%~fF" (
    copy /y "%%~fF" "%STAGE_PACKAGES%\" >nul
    set /a PACKAGE_COUNT+=1
  )
)
echo     %PACKAGE_COUNT% package file^(s^)

echo ==> Writing Release manifest
> "%RELEASE_DIR%\MANIFEST.txt" (
  echo FunctionFoundry Release bundle
  echo GeneratedUTC=%DATE% %TIME%
  echo Host=%OS%
  for /f "delims=" %%V in ('dotnet --version') do echo DotnetSdk=%%V
  echo.
  echo Libraries:
)
for /r "%STAGE_BIN%" %%F in (*) do (
  set "REL=%%F"
  set "REL=!REL:%RELEASE_DIR%\=!"
  >> "%RELEASE_DIR%\MANIFEST.txt" echo !REL!
)
>> "%RELEASE_DIR%\MANIFEST.txt" echo.
>> "%RELEASE_DIR%\MANIFEST.txt" echo Packages:
for /r "%STAGE_PACKAGES%" %%F in (*) do (
  set "REL=%%F"
  set "REL=!REL:%RELEASE_DIR%\=!"
  >> "%RELEASE_DIR%\MANIFEST.txt" echo !REL!
)

echo.
echo Release folder ready: %RELEASE_DIR%
dir /s /b "%RELEASE_DIR%"
exit /b 0
