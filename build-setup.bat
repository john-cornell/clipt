@echo off
setlocal

set SIGN_CERT=%~dp0installer\CliptCodeSigning.pfx
set SIGN_PASS=CliptSign2026
set SIGN_TOOL=signtool

echo === Building Clipt (Release) ===
dotnet build src\Clipt\Clipt.csproj -c Release
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo.
echo === Signing Clipt.exe ===
%SIGN_TOOL% sign /f "%SIGN_CERT%" /p %SIGN_PASS% /fd SHA256 /t http://timestamp.digicert.com "src\Clipt\bin\Release\net8.0-windows\Clipt.exe"
if errorlevel 1 (
    echo SIGNING FAILED
    exit /b 1
)

echo.
echo === Compiling Installer ===
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/SCliptSign=%SIGN_TOOL% sign /f $q%SIGN_CERT%$q /p %SIGN_PASS% /fd SHA256 /t http://timestamp.digicert.com $f" installer\Clipt.iss
if errorlevel 1 (
    echo INSTALLER COMPILE FAILED
    exit /b 1
)

echo.
echo === Done ===
echo Output: installer\Output\CliptSetup.exe
