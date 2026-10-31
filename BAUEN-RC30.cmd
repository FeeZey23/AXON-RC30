@echo off
setlocal
cd /d "%~dp0"
echo ISAS - AXON-RC30: Windows-Build, isolierte UI-Pruefung und MSI-Bau.
echo Die native Pruefung kann kurz die Maus auf ihrem eigenen Testfenster bewegen.
echo Keine produktive Installation und keine Veraenderung von rc26/rc27.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1"
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-Installer.ps1"
if errorlevel 1 exit /b 1
echo MSI gebaut. Installation und Deinstallation zuerst isoliert auf Windows pruefen.
echo Danach installer\README.md beachten. Dieser Lauf installiert nicht.
exit /b 0
