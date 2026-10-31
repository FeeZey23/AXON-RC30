# Windows und Visual Studio einrichten

## Status und Arbeitsordner

Diese Anleitung beschreibt den reproduzierbaren Ablauf. Der Quellordner wurde am 31.10.2026 auf dem Ziel-PC angelegt, gebaut und die MSI anschließend produktiv installiert. Der bereinigte Ordner heißt exakt **AXON RC30 (by ISAS)**. Auf dem Ziel-PC soll er als eigener Ordner innerhalb des bestätigten Projektbereichs entstehen. Den aktiven Runtimeordner nicht als Quellablage überschreiben.

## Voraussetzungen

| Werkzeug | Benötigt für | Hinweise |
|---|---|---|
| Windows x64 | Anwendung, WPF, Installer | Andere Architekturen sind nicht als freigegeben behauptet. |
| Visual Studio mit .NET-Desktopentwicklung | IDE / WPF-Designer | Kompatible Insiders-/Preview-Ausgabe möglich. |
| .NET SDK mit .NET-8-Unterstützung | Restore, Build und Tests | `global.json` erlaubt geeignete neuere Hauptversionen. |
| Microsoft Edge WebView2 Runtime | Browser im Produkt / UI-Test | Evergreen-Runtime über den offiziellen Microsoft-Weg beziehen. |
| Node.js 20 oder kompatibel | Connectorintegration / Cloud-Client | Kein zusätzliches npm-Paket für den eingebetteten Client nötig. |
| PowerShell 5.1 oder neuer | Gepflegte Windows-Skripte | Skripte sind mit UTF-8-BOM für Windows PowerShell gespeichert. |
| Git | Lokale Versionsverwaltung | GitHub-Anmeldung erst für spätere Veröffentlichung nötig. |
| Internetzugang | NuGet, Dotnettool, Arena und Relay | Paket-/Relayverfügbarkeit ist eine externe Abhängigkeit. |

Für das fertige Self-contained-MSI soll beim Endnutzer kein Entwickler-SDK erforderlich sein. Die WebView2-Runtime bleibt eine separate Voraussetzung. Der Installer lädt nicht ungefragt weitere Browserpakete oder bezahlte Dienste nach.

## Visual Studio öffnen

1. Projektordner unverändert an den freigegebenen Windows-Pfad übertragen.
2. `ArenaDesktopCompanion.sln` öffnen.
3. Fehlende Desktopworkload über den offiziellen Visual-Studio-Installer ergänzen.
4. NuGet-Restore abwarten; fehlende Pakete oder Feedfehler nicht durch nicht überprüfte DLL-Downloads ersetzen.
5. `ArenaDesktopCompanion` als Startprojekt wählen.
6. Konfiguration Debug oder Release, Any CPU wählen; die Auslieferung wird ausdrücklich als win-x64 publiziert.
7. Neue RC30-Instanz nicht gleichzeitig gegen dieselben produktiven AppData wie eine andere RC30 starten.

Die Solution enthält WPF, Core, Connector-DLL, Core.Tests und AXON.RC30.Integration. Sie ist absichtlich nicht nach der Produktmarke umbenannt, da mehrere Regressionen sie als Wurzelmarker verwenden.

## Vollständige Windowsprüfung

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
```

Ausführen in einer interaktiven Windows-Sitzung. Der native UI-Test darf kurz die Maus auf seinem eigenen Testfenster bewegen. Er benutzt `RC30.Smoke`, nicht das produktive Browserprofil. Währenddessen keine wichtige andere Mausaktion durchführen. Bei unsicherem Fokus wird die Geste übersprungen; dann fehlt die volle Freigabe.

Ergebnisse: `.artifacts/checks/validation.json`, `core.trx`, `ui.json` und Toollogs. Diese Dateien bleiben privat. Ein passender Quellenhash, alle erforderlichen Tests und kein nativer Skip ergeben das Releasegate. `-SkipUiSmoke` ist nur für reduzierte/headless Prüfung vorgesehen.

## Installer bauen und zunächst isoliert prüfen

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
```

Das Skript prüft zuerst den vollständigen passenden Windowsnachweis, publiziert self-contained, stellt das gepinnte WiX-Tool wieder her und erzeugt das MSI mit Hashmanifest. Die Installerquellen wurden auf Windows mit einem getrennten Installationsziel und RC30.Smoke-Daten geprüft. Dieser Kontext ist keine Betriebssystem-VM; bei Änderungen einen neuen passenden Install-/Deinstalltest durchführen.

Siehe die vollständigen [Installergates](../installer/README.md). Die Bestätigung einer isolierten VM-Abnahme erfolgt ausdrücklich separat; das vorhandene Installationsskript erzeugt oder prüft keine VM von allein.

## Produktiv installieren

Nur nach isolierter Abnahme und Hashprüfung. rc26/rc27 unverändert weiterlaufen lassen. RC30 normal schließen; bei Bedarf kann das Installationsskript mit ausdrücklichem Schalter nur AXON.RC30 sanft schließen. Es erzwingt keinen Kill. Einstellungen werden privat gesichert; Browserprofile bleiben an Ort und Stelle.

```powershell
# Prüfen ohne Installation:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Local.ps1 -Msi releases\ISAS-AXON-RC30-0.2.304-win-x64.msi
# Erst dieser Aufruf installiert:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Local.ps1 -Msi releases\ISAS-AXON-RC30-0.2.304-win-x64.msi -Install -CloseExistingRc30
```

Ein `ExecutionPolicy Bypass` gilt nur für diesen expliziten Skriptprozess und ist kein Abschalten von Defender/SmartScreen. Vorher Quelle prüfen. Unsichere oder unerwartet veränderte Pakete nicht ausführen.

Nach Installation Einstellungen, vorhandene Projekte, Tabs, Verlauf und Browserprofile prüfen. Ein sichtbares Fenster ist noch keine Bestätigung einer reparierten produktiven Relayverbindung.

## Diagnose bei Buildproblemen

- SDKfehler: `dotnet --info`, SDKauswahl und Desktopworkload prüfen.
- NuGetfehler: offiziellen Feed/Netz/Proxy prüfen; keine Live-Secrets in Fehlerposts.
- WebView2 fehlt: Runtime separat offiziell installieren, nicht nur das SDKpaket referenzieren.
- UI ohne native Freigabe: interaktiven Desktop und Fokus prüfen; nicht die Prüfung fälschen.
- WiX-/MSIfehler: `.artifacts/checks/msi-build.log` lokal lesen; diese neue Toolchain ist noch unbestätigt.
- App startet nicht: Crashlog, WebView2 und bereits laufende RC30-Instanz prüfen.
