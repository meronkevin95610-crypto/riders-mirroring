@echo off
REM Build the UxPlayStub as a single-file self-contained uxplay.exe and
REM copy it into vendor\uxplay\ so the Riders Mirroring app picks it up.
REM
REM Usage: tools\UxPlayStub\build-and-copy.cmd
REM
REM Why this exists: the real UxPlay binary (https://github.com/FDH2/UxPlay)
REM requires MSYS2 + the GStreamer SDK to build on Windows, which is way
REM too much yak-shaving for what the app needs (it only consumes UxPlay's
REM stdout). The stub imitates the same wire format so the parsing pipeline
REM works end-to-end. When a real UxPlay.exe is available, drop it into
REM vendor\uxplay\uxplay.exe and the app will pick it up automatically
REM (the AirPlayReceiverProcess binary locator prefers a real UxPlay.exe
REM over the stub if one is present).

setlocal
set "ROOT=%~dp0..\.."
set "STUB=%ROOT%\tools\UxPlayStub"
set "OUT=%ROOT%\vendor\uxplay"

echo === Publishing UxPlayStub ===
pushd "%STUB%"
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%STUB%\bin\Release\net8.0\win-x64\publish"
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    popd
    exit /b 1
)
popd

echo === Copying uxplay.exe to %OUT% ===
copy /Y "%STUB%\bin\Release\net8.0\win-x64\publish\uxplay.exe" "%OUT%\uxplay.exe" >nul
if errorlevel 1 (
    echo ERROR: copy failed.
    exit /b 1
)

dir "%OUT%\uxplay.exe"
echo Done.
endlocal
