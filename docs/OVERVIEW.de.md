# AXON RC30 — Deutsche Beschreibung und Übersicht

[← README / Language selection](../README.md)

## Was ist AXON RC30?

**AXON RC30**, by ISAS, verbindet Arena-Chats und ausdrücklich ausgewählte lokale Windows-Projektordner in getrennten, dauerhaften Arbeitsbereichen. Jeder AXON-Tab besitzt ein eigenes Browserprofil, eine Projektzuordnung, eine Connectoridentität, Logs und ein Panel-Layout.

AXON RC30 ist kein neuer KI-Dienst, keine offizielle Arena-Integration und **keine Betriebssystem-Sandbox**. Dieses Projekt richtet keinen kostenpflichtigen API-Dienst ein. Arena-Konto, Anbieterberechtigungen, Internetzugang und externe Relayverfügbarkeit bleiben gesonderte Voraussetzungen.

### Die wichtigsten Funktionen

- Dauerhafte, voneinander getrennte Tabs, Browserprofile und Projektzuordnungen.
- Neue oder vorhandene Arena-Chats; automatische Erfassung erkannter Chatadressen und Chatnamen.
- Geschlossene Tabs im Verlauf wiederöffnen; dauerhaftes Entfernen als eigene bewusste Aktion.
- **Connect** bereitet einen sitzungsgebundenen Client vor und setzt einen Entwurf ins aktuelle Eingabefeld. **Der Nutzer sendet selbst** – kein automatisches Absenden.
- AES-256-GCM-Transport, explizite Projekt-/Sitzungsbindung und bestätigter Hin-/Rückweg.
- Autorisierte Dateiaktionen, Manifeste, lokale Werkzeuge und persistentes Jobjournal.
- Optionaler Inbox-/Archivimport mit Sicherungen und Konfliktbehandlung. Downloads in die Projekt-Inbox umzuleiten ist standardmäßig aus.
- Dockbare, skalierbare und auskoppelbare Bereiche für Projektdateien, Protokolle, Einstellungen und Informationen.

### Aufbau und private Daten

```text
Arena-Agent-Workspace  <-->  externes Relay  <-->  AXON RC30 unter Windows
                                                    |-- eigenes Browserprofil je Tab
                                                    |-- ausgewählter Projektordner
                                                    |-- Connector und Jobjournal
                                                    `-- Panels, Einstellungen, Diagnose
```

Clouddateien sind nicht automatisch Windows-Dateien. Ein sichtbares Fenster bestätigt keine funktionierende Verbindung. Lokale Werkzeuge laufen mit den Windows-Benutzerrechten; ein Arbeitsverzeichnis isoliert keine Prozesse.

Private Laufzeitdaten liegen unter `%LOCALAPPDATA%\AXON\RC30`. Settings, Profile, Cookies, Paarungsdaten, Rohlogs, Chat-IDs und Sicherungsarchive gehören nicht ins Repository. Der Quellordner heißt **`AXON RC30 (by ISAS)`**, das Projekt und die Anwendung heißen **AXON RC30**.

### Releasehistorie und aktueller Patch

Die folgenden Zahlen gehören zum zuvor geprüften Release **0.2.304**. Der neue Funktionspatch **0.2.306** steht im [Patchlog](../CHANGELOG.md); seine Build-/Installernachweise werden gesondert beim zugehörigen Release bereitgestellt.


- Technische Basis: **0.2.0-rc30 / Fix4**, MSI **0.2.304**, Windows x64.
- Erzwungener Windows-Neubau: **692 Core-, 23 Connector-, 19 Relay- und 75 native UI-Prüfpunkte** bestanden, ohne übersprungene native Geste.
- MSI-Installation, installierte Smoke-UI und Deinstallation im getrennten Ziel geprüft; produktive Installation sowie erhaltene Profile/Projektzuordnungen bestätigt.
- Die frühere Fix4-Basis und ihre Testzahlen sind **historische Baselinewerte**. Der gesondert belegte neue Windowslauf erreichte dieselben Zahlen; nachträgliche Dokumentationsänderungen sind kein neuer MSI-Bau.
- MSI **unsigniert**. SHA-256 bestätigt Integrität, nicht Herausgebervertrauen; keine SmartScreen-Garantie.
- Keine OS-VM-Abnahme, vollständige Upgrade-/Reparatur-/Mixed-DPI-Matrix oder Relay-Verfügbarkeitsgarantie. Produktive Chat-Rückkehr-/In-flight-/Negativabnahme bleibt offen.
- Repository und Release sind **privat**. Die Windows-CI ist nur manuell startbar; eine Dokumentationsveröffentlichung löst keinen Lauf aus. Manuelle Actions können Kontingente verbrauchen.

### Weiterführende Seiten

- [Vollständige deutsche Projektübersicht mit Modulkatalog](../PROJECT.OVERVIEW.md)
- [Schritt.für.Schritt.Anleitung](../docs/STEP-BY-STEP.de.md)
- [Sicherheitsgrenzen](../SECURITY.md) · [Prüfnachweise](../docs/VALIDATION.md)
- [Installer und Quellpaket](https://github.com/FeeZey23/AXON-RC30/releases/tag/rc30-isas-0.2.304)

Lizenz: MIT mit erhaltenem ursprünglichem Copyright und Drittanbieterhinweisen. Historische technische Bezeichner sind Kompatibilitätsdetails, kein anderer Produktname.
