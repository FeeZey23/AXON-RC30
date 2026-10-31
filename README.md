<div align="center">

<img src="docs/images/application-icon.png" width="88" alt="AXON-Anwendungssymbol" />

# AXON RC30 (by ISAS)

**ISAS - AXON-RC30 · Getrennte Arena-Chats und explizit gebundene Windows-Projekte**

**Technische Basis: 0.2.0-rc30 / Fix4**

WPF · WebView2 · .NET 8 · Windows x64 · Connector-DLL · MIT

[Ausführliche Projektseite](PROJECT.OVERVIEW.md) · [Windows einrichten](docs/SETUP-WINDOWS.md) · [Sicherheit](SECURITY.md) · [Installer](installer/README.md) · [Prüfstatus](docs/VALIDATION.md)

</div>

> **Releasestatus 31.10.2026:** Der bereinigte ISAS-Quellbaum wurde auf Windows vollständig neu gebaut. **692 Core-, 23 Connector-, 19 Relay- und 75 native UI-Prüfpunkte bestanden, ohne übersprungene native Geste.** Die MSI wurde gebaut, mit getrenntem Installationsziel und Smoke-Daten installiert/geprüft/deinstalliert und anschließend produktiv installiert und gestartet. Acht vorhandene Browserprofile und die Projektzuordnungen blieben erhalten. **Unsigniert; keine OS-VM-Abnahme und keine Garantie für die externe Relayverfügbarkeit.** GitHub-Veröffentlichung weiterhin privat vorgesehen, Eigentümer/Anmeldung noch ausstehend. [Nachweise](docs/RELEASE.EVIDENCE.json).

![Bedienstruktur von AXON RC30 – schematische Darstellung, keine Bildschirmaufnahme](docs/images/ui-layout.svg)

## Was AXON macht

AXON RC30 stellt Arena in einer Windows-Anwendung mit mehreren unabhängigen Sitzungen dar. Ein AXON-Tab besitzt seine eigene persistente Identität, ein WebView2-Browserprofil, einen gewählten Projektordner, eine Connector-Paarung, eigene Logs und ein eigenes Panel-Layout. Andere Projekte lassen sich in anderen Tabs bearbeiten, ohne dieselben Cookies, Jobs oder Panelobjekte gemeinsam zu verwenden.

Der Connector verbindet den Agent-Workspace in der Cloud mit dem ausgewählten Windows-Projekt. Er ist **keine offizielle Arena-Integration und keine Betriebssystem-Sandbox**. Er ersetzt weder die Arena-Anmeldung noch Agent-Berechtigungen und verspricht keinen uneingeschränkten Toolzugriff.

### Funktionen

- Persistente Tabs mit eigenen Browserprofilen, Projekten und Lebenszyklen.
- Tabreihenfolge, Gruppen, manuelle Benennung oder automatische Erkennung des aktuellen Chatnamens.
- Automatische Speicherung erkannter Arena-Chatadressen; keine manuelle Chatadressmaske im aktuellen Einstellungsbereich.
- Geschlossene Tabs im Verlauf wiederöffnen oder ausdrücklich dauerhaft entfernen.
- Einmalige Übernahme passender Arena-Anmeldecookies beim Erzeugen neuer Tabs; danach getrennte Profile.
- **Connect** erzeugt einen sitzungsgebundenen Node-Client als Capsule und füllt nur den Chatentwurf. **Der Nutzer sendet selbst.**
- AES-256-GCM-Transport, Projektbindung und CONNECT-/ACK-Rundlauf.
- Projektdateien lesen/schreiben, Manifeste abrufen und vorgesehene lokale Werkzeuge ausführen, mit Protokoll-, Pfad- und Jobprüfungen.
- Optionaler Inbox-/Archivimport mit Sicherungen und Konfliktbehandlung.
- Downloads in die Projekt-Inbox umleiten ist standardmäßig **aus**; dies ist von automatischer Inbox-Verarbeitung zu unterscheiden.
- Dockbare, skalierbare und auskoppelbare Panels für Dateien, Protokolle, Einstellungen und Informationen.
- Settings-Strukturprüfung, Wiederherstellung und isolierte Fehlerbehandlung pro Tab.
- Persistentes Jobjournal gegen blindes Wiederholen nach Neustart.

## Grenzen

Ein geöffnetes Fenster bedeutet nicht automatisch eine funktionierende Verbindung. Das dateibasierte externe Relay kann ausfallen. Wiederholtes Connect repariert nicht grundsätzlich HTTP 403, 429, 503 oder 507.

Es gibt keinen automatischen Chatversand und keine Windows-Prozesssandbox. Ein übernommener experimenteller DOM-Beobachter ist standardmäßig aus und kein als freigegeben behaupteter offizieller Arena-Zugriffsweg; Details in SECURITY.md. Ein Arbeitsverzeichnis ist keine Zugriffsisolation; gestartete Werkzeuge arbeiten mit den Rechten des angemeldeten Benutzers. WebView2 schreibt während normaler Nutzung Browserdaten – Profile sind nicht bitweise eingefroren.

Ein MSI-Produktname „ISAS“ ist noch keine verifizierte digitale Herausgeberidentität. Hashes, Tests und Signaturen erfüllen unterschiedliche Aufgaben.

## Visual Studio / Insiders / Preview

Den Ordner **`AXON RC30 (by ISAS)`** zusammenhalten und darin **`ArenaDesktopCompanion.sln`** öffnen. Der historische Solution-Dateiname und interne Namespaces bleiben absichtlich stabil. Produktname und neuer Projektordner tragen die ISAS-Bezeichnung.

Voraussetzungen: Windows x64, ein .NET-8-fähiges SDK und Visual Studio mit **.NET-Desktopentwicklung**. Ein kompatibles neueres SDK kann über `global.json` verwendet werden; Preview-/Insiders-SDKs sind zugelassen. Es handelt sich um ein echtes WPF-Projekt, nicht um ein VS-Code-Webprojekt. Das Startprojekt ist `ArenaDesktopCompanion`; die ausführbare Datei heißt `AXON.RC30.exe`.

```text
AXON RC30 (by ISAS)/
├── ArenaDesktopCompanion.sln      # Drei Produktprojekte + beide Testprojekte
├── Directory.Build.props         # Gemeinsame Produkt-/Buildmetadaten
├── global.json                   # SDK-Auswahl mit Roll-forward
├── README.md                     # GitHub-Einstieg
├── PROJECT.OVERVIEW.md            # Technische Projektseite
├── LICENSE / THIRD-PARTY-NOTICES.md
├── SECURITY.md / CONTRIBUTING.md
├── docs/                         # Setup, Betrieb, Abnahme und Bilder
├── src/
│   ├── ArenaDesktopCompanion/     # WPF, Browser, Tabs, Panels und Host
│   ├── ArenaDesktopCompanion.Core/ # Protokoll, Settings, Dateien und Sync
│   └── AXON.Connector/            # Capsule und eingebetteter Node-Client
├── tests/
│   ├── ArenaDesktopCompanion.Core.Tests/
│   └── AXON.RC30.Integration/
├── scripts/                      # Gepflegte Build-/Installationsabläufe
├── installer/                    # WiX-MSI-Quellen
├── .config/                      # Gepinnte lokale Entwicklerwerkzeuge
└── .github/workflows/            # Nur manuell gestartete Windows-CI
```

`bin/`, `obj/`, `.artifacts/` und `releases/` werden beim Bauen erzeugt und sind ignoriert. Persönliche Konfiguration, Logs, Profile und Zugangsdaten sind keine Quellbestandteile.

Alternativ startet `BAUEN-RC30.cmd` den vollständigen Windows-Test und anschließend den MSI-Bau, **ohne** produktive Installation. Der Ablauf wurde auf dem Ziel-PC geprüft; Änderungen erfordern einen neuen passenden Testlauf.

## Auf Windows bauen und prüfen

Befehle in der Projektwurzel ausführen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
```

`Build.ps1` führt Privacy-Vorprüfung, Restore, Debug-/Release-Build mit Warnungen als Fehler, Core-Tests, C#↔Node-Integration, Relayregressionen und den dedizierten WPF-/WebView2-Smoke-Test aus. Ergebnisse entstehen unter `.artifacts/checks/`.

**UI-Test:** Zeigt eigene Prüffenster und kann kurz die Maus darin bewegen. Nicht während anderer wichtiger Eingaben ausführen. Er verwendet getrennte `RC30.Smoke`-Daten und eine lokale Testseite, nicht den produktiven Arena-Chat. Ist der native Maus-Drag nicht sicher durchführbar, wird er übersprungen; das vollständige Releasegate bleibt dann geschlossen.

Für Headless-/CI-Prüfungen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1 -SkipUiSmoke
```

Das ist **keine Installerfreigabe**. Automatische Push-Workflows sind bewusst nicht aktiviert. Manuelle GitHub-Actions-Läufe können Kontingente verbrauchen; keine Billing-Freigabe oder kostenpflichtiger Dienst wird automatisch eingerichtet.

## Windows-Installer

Vorbereitet ist WiX 4.0.6 mit eingebettetem CAB, Per-User-Installation unter `%LOCALAPPDATA%\Programs\ISAS\AXON-RC30` und Startmenüeintrag. Die Payload enthält .NET selbst; **WebView2 Runtime** bleibt erforderlich. Die MSI-Version 0.2.304 wurde gebaut, im separaten Testziel installiert und deinstalliert sowie danach produktiv installiert.

Nach erfolgreichem vollständigem Windows-Build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
```

Vor Installation die Gates in [installer/README.md](installer/README.md) lesen. Das Installationsskript bleibt ohne `-Install` ein Dry Run. Ein SHA-256-Hash bestätigt Integrität, nicht Herausgebervertrauen. Ein unsigniertes MSI kann Windows-/SmartScreen-Warnungen zeigen.

## Alltag

1. AXON starten und Arena im eingebetteten Browser anmelden.
2. Für ein getrenntes Projekt einen neuen AXON-Tab anlegen oder einen vorhandenen wählen.
3. Den tatsächlichen Windows-Projektordner auswählen, keinen internen `.arena-inbox`-Ordner.
4. Den gewünschten Arena-Chat in diesem Tab öffnen.
5. **Connect** drücken; den eingesetzten Entwurf prüfen und selbst absenden.
6. Erst nach bestätigtem Rundlauf Projektaktionen erwarten.
7. Für ein anderes unabhängiges Projekt einen anderen AXON-Tab verwenden.

Eine andere Arena-Chat-ID im **selben** Tab ist sicherheitsrelevant. Fix4 kann dabei die bisherige Paarung verwerfen. Zurücknavigieren stellt sie nicht automatisch wieder her. Die Anzeige „Kontakt älter als drei Minuten“ dagegen bedeutet nicht, dass der Dienst beendet wurde. Siehe [Betrieb und Diagnose](docs/OPERATIONS.md).

## Datenschutz und Status

Private Laufzeitdaten liegen unter `%LOCALAPPDATA%\AXON\RC30`, nicht im Repository. Settings, Browserprofile, Sitzungslogs, Journale und Paarungsnachweise können Secrets, Chatadressen, Cookies und persönliche Pfade enthalten. Niemals ungeprüft veröffentlichen.

RC30 verschlüsselt Transportinhalte. Dateinamen, Mengen, Zeiten und Zugriffsmuster können dem Relay dennoch sichtbar sein. Malware im Benutzerkontext wird durch eine reine Ordnerstruktur nicht ausgesperrt.

Die **frühere Fix4-Basis** bestand 692 Core-Tests, 23 Connector-Prüfpunkte, 19 Relay-/Redirect-Regressionen und 75 native UI-Prüfungen. Dies sind **historische Baselinewerte**. Der erzwungene neue Windowslauf erreichte dieselben Zahlen; die gesonderten Installer- und Installationsnachweise stehen im Prüfbericht. Reale Relayausfälle, sichere Chat-Rückkehr, produktive Negativnachweise und Mixed-DPI-Hardwaretests bleiben offen: [VALIDATION.md](docs/VALIDATION.md).

## Projektseite, GitHub und Lizenz

GitHub zeigt standardmäßig diese README. **[PROJECT.OVERVIEW.md](PROJECT.OVERVIEW.md)** enthält die vollständige Architektur, Bilder, Abläufe, Datenmodell, Sicherheitsgrenzen, Quellmodulkatalog und Abnahmematrix. Eine eigene Pages-Website ist nicht eingerichtet.

Vorgesehen ist das private Repository **`AXON-RC30`** unter einem bestätigten Eigentümeraccount. Veröffentlichung erst nach Datenschutzprüfung, Windows-Installerabnahme und erfolgreicher lokaler Installation. Noch kein Online-Repository wird behauptet. Installer gehören später in Release-Assets, nicht als Binärdateien in die Quellhistorie.

Lizenz: [MIT](LICENSE), mit erhaltenen ursprünglichen Copyright-Hinweisen. Siehe [Drittanbieterhinweise](THIRD-PARTY-NOTICES.md). ISAS/AXON beschreibt hier das Projekt; eine offizielle Verbindung zu Arena oder Microsoft wird nicht behauptet.
