# AXON RC30 — Schritt.für.Schritt.Anleitung

[← README / Language selection](../README.md)

## Schritt.für.Schritt.Anleitung

Diese Anleitung führt durch Installation, erste bestätigte Verbindung, Alltag, Wiederaufnahme und optionale Entwicklung. **Das Projekt und die Anwendung heißen AXON RC30.**

### 1. Entscheiden: Anwendung nutzen oder entwickeln

- **AXON RC30 nutzen:** Windows x64, Internetzugang, ein Arena-Konto und Microsoft Edge WebView2 Runtime werden benötigt. Das Release-MSI enthält .NET; Visual Studio oder ein Entwickler-SDK sind zum normalen Nutzen nicht erforderlich.
- **AXON RC30 bauen oder ändern:** zusätzlich Visual Studio mit **.NET-Desktopentwicklung**, ein zu `global.json` passendes SDK, Git und Node.js 20 oder kompatibel installieren. Der Entwicklerabschnitt folgt unten.
- WebView2 ausschließlich über [Microsofts offizielle WebView2-Seite](https://developer.microsoft.com/microsoft-edge/webview2/) beziehen. Gerätesicherheitsrichtlinien beachten; Defender oder SmartScreen nicht abschalten.

### 2. Installer herunterladen und prüfen

1. Bei GitHub mit einem für dieses **private Repository** berechtigten Konto anmelden. Ein 404 ohne Anmeldung bedeutet nicht, dass das Release gelöscht wurde.
2. Das [geprüfte Windows-Release](https://github.com/FeeZey23/AXON-RC30/releases/tag/rc30-isas-0.2.307) öffnen.
3. Unter **Assets** `ISAS-AXON-RC30-0.2.307-win-x64.msi` und die zugehörige `.sha256`-Datei herunterladen. Das Source-ZIP enthält Quellen und ist nicht der Installer.
4. PowerShell im Downloadordner öffnen und für **genau dieses Release** prüfen:

```powershell
$msi = '.\ISAS-AXON-RC30-0.2.307-win-x64.msi'
$expected = ((Get-Content -LiteralPath ($msi + '.sha256') -Raw).Trim() -split '\s+')[0]
if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid SHA-256 file.' }
$actual = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'SHA-256 stimmt nicht. Nicht installieren.' }
'Erwarteter SHA-256 stimmt.'
```

5. Dieser Sollhash gilt nicht automatisch für spätere MSI-Versionen. Hashdatei und GitHub-Asset-Digest erlauben zusätzliche Vergleiche. Ein übereinstimmender Hash ist **keine digitale Herausgebersignatur**. Das MSI ist unsigniert.

### 3. AXON RC30 installieren und starten

1. Bei einem RC30-Update Arbeit speichern und **RC30 normal schließen**. rc26/rc27 nicht beenden oder ersetzen. Private RC30-Daten bei geschlossener Anwendung aus `%LOCALAPPDATA%\AXON\RC30` an einen privaten Ort sichern; Projektdateien getrennt sichern.
2. Das geprüfte MSI doppelklicken und dem Windows-Installer folgen. Einen unerwarteten Sicherheitsrichtlinienblock oder Hashfehler nicht übergehen.
3. Die benutzerbezogene Installation liegt unter `%LOCALAPPDATA%\Programs\ISAS\AXON-RC30`. **AXON RC30** über den Startmenüeintrag oder dort über `AXON.RC30.exe` starten.
4. Prüfen, ob das Fenster erscheint und vorhandene Tabs/Profile/Projektzuordnungen erhalten sind. Ein offenes Fenster bestätigt noch keine Connectorverbindung.
5. Startet der eingebettete Browser nicht, zuerst WebView2 Runtime und private Diagnose prüfen, statt Daten zu löschen oder blind neu zu installieren.

**Für Wartung/Entwicklung:** `scripts/Install-Local.ps1` ist kein alleinstehender Download-Installer. Es verlangt zusätzlich die passende lokale `installer-manifest.json` und für `-Install` den passenden privaten Installations-/Smoke-/Deinstallnachweis. Die Release-Datei `.sha256` ersetzt dieses Manifest nicht. Diese Gates nicht entfernen; für normale Installation das veröffentlichte MSI verwenden.

### 4. Arbeitsbereich anlegen oder auswählen

In **Einstellungen** die Sprache **Deutsch** oder **English** wählen, bei Bedarf GitHub über die offizielle Gerätecode-Seite verbinden und den offiziellen Repository-Link öffnen. Zugangsdaten werden nicht in AXON-Settings gespeichert. Updateprüfung und Installation sind getrennt: **Update im Repository prüfen**, danach bei angebotenem Update **Update** drücken; nicht während laufender Jobs aktualisieren. Neue Arbeitsbereiche starten nur lesend; das benötigte Berechtigungsprofil bewusst wählen. Lokale Programme haben Windows-Benutzerrechte, keine OS-Isolation.

**Connect** ohne Projekt öffnet einen Dialog: vorhandener Ordner, HTTPS-GitHub-Link oder automatisch erstellen. Automatische Projekte entstehen im *tatsächlichen Programmverzeichnis* unter `.axon/projects/0001.axon.dd-MM-yyyy-HH-mm` mit dauerhaft aufsteigender Nummer. Keine automatische Adminfreigabe und kein stiller Ausweichordner. GitHub-Klonen benötigt Git und führt heruntergeladenen Code nicht aus.


1. Einen vorhandenen AXON-Tab auswählen oder über **＋** einen unabhängigen Arbeitsbereich anlegen.
2. **Einstellungen** öffnen und den tatsächlichen Windows-Ordner des gewünschten Projekts auswählen. Nicht `.arena-inbox`, ein Browserprofil oder den Installationsordner als Abkürzung wählen.
3. **Vor jedem Schreib-/Buildauftrag** sichtbaren Tab und Projektpfad prüfen.
4. Im eingebetteten Browser dieses Tabs bei Arena anmelden und einen neuen oder vorhandenen Agent-Mode-Chat öffnen. Erkannte Chatadressen und Namen werden automatisch gespeichert; eine manuelle Chatadressmaske ist nicht erforderlich.
5. Für ein anderes unabhängiges Projekt einen weiteren AXON-Tab verwenden. Ein anderer Arena-Chat innerhalb desselben Tabs ist nicht dieselbe Isolationsgrenze.

### 5. Connect ausführen und Rundlauf bestätigen

1. Im richtigen AXON-Tab und Arena-Chat **Connect** drücken.
2. Den im aktuellen Chat-Eingabefeld eingesetzten Text prüfen. Die Capsule enthält Sitzungs-/Projekt-Paarungsdaten: privat behandeln und nicht als Beispiel in Issues veröffentlichen.
3. **Den Entwurf selbst absenden.** AXON RC30 sendet nicht automatisch. Nur Zwischenablageinhalt oder ein fehlender Entwurf ist kein erfolgreicher Connect.
4. Den Agenten den mitgelieferten Client einrichten und einen frischen Status-/Handshake-Rundlauf durchführen lassen. Der erzeugte Client benötigt eine kompatible Node.js-Laufzeit im Agent-Workspace; Windows- und Cloud-Voraussetzungen sind getrennt.
5. Den tatsächlichen Windows-Projektpfad bestätigen lassen und zuerst eine harmlose Leseaktion/Dateiliste anfordern. Antwort mit dem gewählten Projekt vergleichen.
6. Erst danach geplante Schreib-/Buildaktionen freigeben. Keine Passwörter oder API-Tokens im Chat teilen; unbelegtes „verbunden“ nicht als Nachweis akzeptieren.

### 6. Alltag und spätere Wiederaufnahme

Neue Tabs fragen, ob die Anmeldung gemerkt werden soll: **Ja** behält das unabhängige Profil, **Nein** verwendet ein echtes InPrivate-Profil. Bestehende Profile bleiben erhalten. Externe Links öffnen weitere WebView-Fenster; das Hauptfenster bleibt bei Arena. OAuth-Popups behalten nativen Opener und Profil. Anbieterrestriktionen gelten weiterhin; keine Loginbeschränkungen werden umgangen.

Der Assistent **Projekt als GitHub-Repository veröffentlichen** benötigt GitHub-Anmeldung und Git. Name/Beschreibung eingeben, privaten Standard bewusst beibehalten oder ändern, Dateiliste prüfen, Dateien ausdrücklich auswählen und die Abschlussübersicht bestätigen. Er erstellt nur neue Repositories, überschreibt keine vorhandenen und verändert nicht die Git-Historie deines Projektordners. Dateien zusätzlich selbst prüfen: Kein Scanner garantiert vollständige Secretfreiheit.


1. **Projektdateien**, **Protokolle**, **Einstellungen** und **Info** öffnen die jeweiligen Panels. Diese lassen sich andocken, vergrößern und auskoppeln.
2. Unabhängige Arbeiten in getrennten AXON-Tabs halten und vor folgenreichen Aktionen das Projekt bestätigen. Lokale Befehle nutzen Windows-Benutzerrechte, keine Betriebssystem-Prozessisolation.
3. Vorhandene Tabs/Profilidentitäten werden gespeichert. Nach einem Neustart Tab, Chat und Projekt prüfen und vor folgenreichen Aktionen frischen Kontakt bestätigen. Gespeicherte Einstellungen allein beweisen keine aktuelle Verbindung.
4. Unter **Verlauf ▾** geschlossene Tabs wiederöffnen. Dauerhaftes Entfernen ist eine eigene bewusste Aktion; Projekt oder Browserprofil nicht vorschnell zur Fehlersuche löschen.
5. Eine andere Arena-Chat-ID **im selben Tab** kann in Fix4 die bisherige Paarung verwerfen. Zurücknavigieren stellt sie nicht automatisch wieder her. Bei Bedarf bewusst neu verbinden und prüfen.
6. Downloadumleitung in die Projekt-Inbox ist **standardmäßig aus**. Optionale Inbox-Verarbeitung bewusst aktivieren, Konflikte prüfen und Sicherungen behalten.

### 7. Wenn etwas nicht funktioniert

| Beobachtung | Sichere Reaktion |
|---|---|
| Kontakt alt / älter als drei Minuten | Frischen Rundlauf prüfen. Das bedeutet allein nicht, dass der lokale Dienst beendet ist. |
| Connect setzt keinen Entwurf ein | Richtigen Chat und Eingabefokus prüfen. Nicht automatisch absenden oder allein aus der Zwischenablage Erfolg ableiten. |
| HTTP 403 | Verweigerung dokumentieren; keine Zugangsdaten raten und nicht endlos wiederholen. |
| HTTP 404 | Konkreten Endpunkt, Datei und Zugriffskontext prüfen; eine fehlende Datei beweist keinen Appstillstand. |
| HTTP 429 | Häufigkeit reduzieren, begrenzte Wiederholungen statt Retry-Sturm. |
| HTTP 503 / 507 | Mögliche Relay-Verfügbarkeits-/Speicherstörung; ohne Nachweis keinen Windows-Plattenmangel behaupten. |
| Jobtimeout | Die Aktion könnte bereits ausgeführt sein. Vor erneutem Start bekannte Job-ID, Journal und lokalen Zustand prüfen; keine blinden doppelten Installationen/Builds/Schreibaktionen. |
| SDK oder WebView2 fehlt | Passende offizielle Abhängigkeit installieren; keine unbekannten Ersatz-DLLs herunterladen. |

Logs zuerst privat lesen. Vor Weitergabe Geheimnisse, Cookies, Chat-IDs, persönliche Pfade und Profildaten entfernen. Siehe [Betrieb und Wiederaufnahme](../docs/OPERATIONS.md) und [Sicherheit](../SECURITY.md).

### 8. Quellprojekt in Visual Studio öffnen und bauen

1. Für den aktuellen Quellen-/Dokustand im Repository auf Branch `main` **Code → Download ZIP** wählen oder mit einem berechtigten Git-Client klonen. Das vorhandene Release-Quellpaket ist eine Momentaufnahme seines Release-Tags, nicht automatisch die neueste Dokumentation.
2. In einen separaten Ordner **`AXON RC30 (by ISAS)`** entpacken. Installierte Runtime und private AppData nicht überschreiben.
3. **[`AXON RC30.sln`](../AXON%20RC30.sln)** in Visual Studio oder kompatibler Insiders-/Preview-Ausgabe öffnen. Bei Bedarf **.NET-Desktopentwicklung** installieren und NuGet-Restore abwarten.
4. **AXON RC30** als Startprojekt wählen. Core, Connector und Testprojekte sind enthalten. Keine zweite RC30 gegen dieselben produktiven Zustandsdaten starten, während eine andere läuft.
5. `ArenaDesktopCompanion.sln`, interne Verzeichnisse und Namespaces bleiben Kompatibilitätsaliase für vorhandene Tests/Buildwerkzeuge. Sie sind nicht der Produktname und sollten nicht blind umbenannt werden.
6. PowerShell in der Quellwurzel öffnen, Skripte prüfen und ausführen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
```

Der vollständige Build enthält Restore, nichtinkrementelle Debug-/Release-Builds, Core-/Connector-/Relayprüfungen und einen eigenen nativen UI-Smoke-Test. **Dieser öffnet eigene Prüffenster und bewegt darin kurz die Maus.** Interaktiven Desktop verwenden und währenddessen keine wichtige andere Eingabe machen. Das Smoke-Profil ist vom produktiven Profil getrennt. `.artifacts/checks/` privat prüfen; übersprungene native Gesten lassen das Releasegate geschlossen.

`BAUEN-RC30.cmd` baut nach erfolgreicher Prüfung zusätzlich das MSI, installiert es aber **nicht produktiv**. `Build-Installer.ps1` verlangt passende vollständige Windowsnachweise; neu gebautes MSI vor Installation prüfen. `Build.ps1 -SkipUiSmoke` ist reduzierte/headless Prüfung, keine vollständige Installerabnahme. Ein geänderter Quelldigest verlangt vor neuem Installerbau erneut passende Prüfungen.

`ExecutionPolicy Bypass` gilt hier nur für den expliziten Skriptprozess und ist keine Aufforderung, Windows-Sicherheit abzuschalten. Die optionale GitHub-CI ist nur manuell startbar; keinen Lauf auslösen, wenn keine Actions-Kontingente verbraucht werden sollen. Details: [Windows-Setup](../docs/SETUP-WINDOWS.md) und [Installergates](../installer/README.md).

### 9. Abschlusskontrolle

- [ ] Gewünschtes Release/Branch gewählt; bei Installation MSI-SHA-256 geprüft.
- [ ] App startet, WebView2 funktioniert, Profile und Projekte sind erhalten.
- [ ] Richtiger AXON-Tab, Arena-Chat und Windows-Projektordner bestätigt.
- [ ] Connect-Entwurf geprüft und **selbst abgesendet**.
- [ ] Frischer Rundlauf und harmlose Projekt-Leseaktion vor Schreibaufträgen bestätigt.
- [ ] Vertrauliche Einstellungen, Logs und Profile bleiben außerhalb von Git.

Das Release bleibt unsigniert; es gibt keine vollständige VM-/Upgrade-/Mixed-DPI-Zertifizierung. Externe Relaystabilität sowie produktive Chat-Rückkehr-/In-flight-/Negativabnahme sind nicht garantiert. [Aktuelle Nachweise](../docs/RELEASE.EVIDENCE.json) nutzen, nicht allein eine grüne Statusanzeige.

