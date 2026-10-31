# Windows-Installer: Entwurf und Freigabegates

**Windowsnachweis vorhanden:** MSI 0.2.304 wurde gebaut, in getrenntem Installationsziel mit RC30.Smoke geprüft und deinstalliert; danach produktiv installiert und mit sichtbarem Fenster gestartet. Vorhandene Profile und Projektzuordnungen blieben erhalten. **Unsigniert; der Test war keine OS-VM.** Details: [Release-Nachweise](../docs/RELEASE.EVIDENCE.json).

- Technologie: WiX Toolset 4.0.6, lokal gepinnt über `.config/dotnet-tools.json`.
- ProductName: `ISAS - AXON-RC30`; Manufacturer-Text: `ISAS`.
- MSI-Version standardmäßig `0.2.304` (eigenes dreiteiliges Windows-Installer-Versionsschema für die RC30/Fix4-Basis).
- Ziel: Windows x64, pro Benutzer unter `%LOCALAPPDATA%\Programs\ISAS\AXON-RC30`.
- Payload: Self-contained .NET 8, kein separat vorausgesetztes .NET SDK beim Endnutzer; WebView2 Runtime bleibt erforderlich.
- Startmenü: ISAS / ISAS - AXON-RC30.
- Programmeinstellungen bleiben unter `%LOCALAPPDATA%\AXON\RC30`; keine automatische Übernahme von rc26/rc27.
- Kein API-Key, kein bezahlter Installer-Dienst. Abhängigkeiten werden beim Bauen von offiziellen Paketquellen geladen.
- Keine Digital-Signatur ohne bereitgestellten Signierweg. Ein SHA-256-Hash ersetzt keine Authenticode-Signatur und keine SmartScreen-Reputation.

## Reihenfolge auf Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Local.ps1 -Msi releases\ISAS-AXON-RC30-0.2.304-win-x64.msi
# Erst der explizite Install-Schalter installiert; vorheriger Aufruf ist ein Dry Run:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Local.ps1 -Msi releases\ISAS-AXON-RC30-0.2.304-win-x64.msi -Install -CloseExistingRc30
```

Vor Installation auf einer produktiven Maschine ist zusätzlich die Installation/Deinstallation in einem separaten Windows-Testkontext (vorzugsweise VM oder Windows Sandbox) zu prüfen. Das Skript selbst erstellt keine VM und behauptet keine Sandbox-Abnahme. Auf dem Produktiv-PC dürfen rc26/rc27 nicht beendet werden. Das explizite Schließen betrifft nur Prozesse namens AXON.RC30; bei mehreren fremden RC30-Installationen vorher manuell die richtige Instanz schließen.

`Build-Installer.ps1` verlangt eine zum Quellbaum passende Windows-Abnahme einschließlich UI und ohne übersprungene native Maus-Geste. Ein CI-Lauf mit `-SkipUiSmoke` erfüllt das Releasegate nicht. Das gebaute MSI wird mit Hash und einem Manifest als **noch nicht installationsverifiziert** ausgegeben. Erst ein erfolgreicher lokaler Installationslauf schreibt die Installationsbestätigung unter `.artifacts/checks`.

## Rückweg / Deinstallation

Deinstallation über Windows „Installierte Apps“. Die MSI-Quellen enthalten keine RemoveFile-Aktion auf Nutzerprojekte oder AppData-Profile. Ein Nachweis der tatsächlichen Deinstallation ist trotzdem Teil der noch offenen VM-Abnahme. Die frühere Projektordner-Runtime bleibt bis zum erfolgreichen Installations- und Rückwegtest als private Rückfallversion erhalten. Sie nicht parallel mit der neuen Installation gegen dieselben RC30-AppData starten.

## Signieren

Optionales externes Signieren mit einem vertrauenswürdigen Code-Signing-Zertifikat oder Signierdienst ist außerhalb dieser Skripte vorzusehen. Private Schlüssel/PFX-Dateien und Passwörter gehören nicht ins Repository und nicht in den Chat. Ohne Signierung wird der Publisher-Text „ISAS“ nicht zu einem kryptografisch verifizierten Herausgeber.

## Automatisierter MSI-Test bei fehlender VM

`scripts/Test-Installer.ps1 -Msi <Datei>` installiert dieselbe MSI mit getrenntem INSTALLFOLDER, führt die tatsächlich installierte Anwendung mit `RC30.Smoke` aus und deinstalliert anschließend. Es prüft produktive Settingshashes und vorherige Prozesse. Das ist **keine OS-VM-Isolation**: Die temporäre MSI-Registrierung und Startmenüeinträge entstehen im aktuellen Benutzerkonto. Der Test stoppt bei einer vorhandenen ISAS-MSI-Installation. Auf dem Ziel-PC sind derzeit weder Sandbox-Executable noch Hyper-V-Modul vorhanden. Ein VM-Nachweis bleibt gesondert offen; er wird nicht vorgetäuscht.

## Verlagerbares Installationsziel

Das tatsächlich verwendete INSTALLFOLDER wird benutzerbezogen gespeichert und für Reparatur/Deinstallation erneut gesucht. Der MSI-Test verlangt das Verschwinden des Test-Executables nach Deinstallation; ein bloßer Exitcode 0 reicht nicht. Ein früher Test fand hier einen Fehler (Deinstallation suchte den Standardpfad statt des Testpfads); diese Fehlerklasse ist jetzt ausdrücklich Teil der Abnahme.
