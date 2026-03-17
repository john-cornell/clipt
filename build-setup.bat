@echo off
setlocal

echo === Building Clipt (Release) ===
dotnet build src\Clipt\Clipt.csproj -c Release
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo.
echo === Compiling Installer ===
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Clipt.iss
if errorlevel 1 (
    echo INSTALLER COMPILE FAILED
    exit /b 1
)

echo.
echo === Done ===
echo Output: installer\Output\CliptSetup.exe
