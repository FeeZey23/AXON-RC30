# AXON RC30 — Patchlog

## 0.2.307 — Funktionsausbau

0.2.305 war ein nur intern installierter, nicht veröffentlichter Abnahmestand. Der echte private GitHub-Klontest deckte dessen Windows-`NUL`-Pfadinkompatibilität auf. 0.2.306 ergänzte die reguläre Git-Policydatei, blieb aber unveröffentlichter Abnahmestand: GitHub normalisiert Upload-Leerzeichen zu Punkten. 0.2.307 bindet deshalb den festen Remote-Assetnamen `AXON.RC30.Installer.exe` an den lokalen Namen `AXON RC30 Installer.exe`, prüft weiterhin beide SHA256-Gegenstellen und benennt nur die erfolgreich geprüfte Downloadkopie lokal um.

### Anwendung und Projekte
- Gespeicherte Sprachauswahl Deutsch/Englisch für Anwendungsbedienung und neue Dialoge. Diagnose-/Protokoll-, Betriebssystem- und Werkzeugmeldungen behalten ggf. ihre Ursprungssprache. Nutzerdaten wie selbst gewählte Tabnamen werden nicht übersetzt.
- Connect ohne Projekt öffnet eine Auswahl: vorhandener Ordner, GitHub-Repository-Link oder automatisch anlegen.
- Automatische Projekte unter `[Programmverzeichnis]\.axon\projects\NNNN.axon.dd-MM-yyyy-HH-mm`; persistenter gesperrter Zähler, kein Überschreiben, kein stiller Ausweichpfad bei fehlenden Schreibrechten.
- GitHub-Klonen per HTTPS ohne automatische Codeausführung; Git bleibt Voraussetzung für Klonen/Upload.
- GitHub-Upload-Assistent als eingebautes `IGitHubPublisher`-Modul: persönliches Konto, Name, Beschreibung, private Sichtbarkeit als Standard, explizite Dateiauswahl und Abschlussbestätigung. Vorhandene Repositories werden nicht ersetzt; Nutzer-Git-Historie/Remotes nicht verändert.

### GitHub und Updates
- Offizieller Repository-Link und offizielle GitHub-CLI-Browseranmeldung. CLI 2.103.3 wird nach SHA-256-Prüfung mitgeliefert; Zugangsdaten bleiben im GitHub-CLI-Schlüsselspeicher, nicht in AXON-Settings oder Git.
- Repository-Updateprüfung über authentisierte GitHub-API, einschließlich kompatibler Vorabversionen. Nur höhere MSI-Versionen, festes Produkt und passender Assetname erlaubt.
- Updatebutton lädt den echten MSI-enthaltenden Burn-Bootstrapper aus dem offiziellen privaten Repo, prüft MSI-/EXE-Manifest gegen die GitHub-Asset-Digests sowie erneut die lokale EXE vor Installation. Damit bleiben auch Bundle-Upgrade-/Deinstallationsregistrierungen konsistent. Normaler Abschluss/Neustart von RC30; kein erzwungenes Beenden aktiver Aufträge, kein Auto-Update allein durch Prüfung.
- Hashprüfung ist keine Herausgebersignatur. MSI/EXE bleiben ohne Signierzertifikat unsigniert.

### Browser und Berechtigungen
- Arena-Haupt-WebView bleibt auf HTTPS-Arena-Hosts. Weiterführende externe Navigation und neue Fenster öffnen eigene WebView-Fenster mit sichtbarer Adresse.
- Native NewWindow-Deferrals/Opener, Browserumgebung, Profil und InPrivate-Eigenschaft werden für OAuth-Popups erhalten. Authentifizierungsschutz des Anbieters wird nicht umgangen; reale Anbieterfreigabe ist nicht durch synthetische Tests ersetzt.
- Neue Tabs fragen nach „Anmeldung merken“; Nein verwendet echte InPrivate-Profile. Bestehende Profile bleiben standardmäßig erhalten. Abschalten in Einstellungen löscht Browserdaten und wirkt auf den Browsermodus beim nächsten Öffnen des Tabs.
- Berechtigungsprofile Lesen / Projektdateien ändern / lokale Programme; neue Tabs standardmäßig nur lesen, vorhandene Konfigurationen kompatibel. Keine Rückfrage pro Auftrag, keine OS-Sandbox.
- Begrenzte FIFO-Hintergrundqueue für Befehle und Schreibaufträge pro Sitzung; Ping/Lesen/Lebenszeichen bleiben verarbeitbar. Bindungs-/Berechtigungsprüfung vor Ausführung, kein blindes Replay.

### Entwicklung, Prüfung und Installer
- Panel-Drag startet nur nach eigenem Druck im Header; kein unbeabsichtigter Drag beim bloßen Überfahren mit gehaltenem Mausknopf. Native Prüfvorbereitung vergrößert ausschließlich ihr eigenes Smoke-Fenster auf dem größten Monitor und wartet auf WPF-Hit-Testing, statt einen verdeckten Startpunkt anzuklicken.
- Eine Versionsquelle `Version.props`; stabiles SDK in `global.json` festgelegt.
- Gepflegte kanonische Sprachseiten erzeugen die vier README-Dropdowns; Konsistenz-/Linkprüfung im Build.
- SHA256-gepinnter Gitleaks 8.30.1 prüft Quellen und sämtliche lokale Git-Refs sowie das kopierte Upload-Snapshot vor Repositoryerstellung/Push. Beim Upload erzwingt die App ihre mitgelieferte Regelkonfiguration; Projekt-Allowlisten und Inline-Ausnahmen können das Gate nicht abschalten. Nur exakt bekannte öffentliche Testvektoren werden eng ausgenommen; keine pauschale Ausnahme für Tests.
- Erweiterte Core- und native Produktregressionen. Ergebniszahlen/Assethashes des jeweiligen Release stehen im veröffentlichen Abnahmenachweis; keine Prüfung durch diesen Patchlog vorgetäuscht.
- Echte WiX-4-Burn-EXE `AXON RC30 Installer.exe` mit eingebettetem MSI. Isolierte Smoke-Installeridentität schützt die produktive MSI während Install-/UI-/Deinstalltests; dies ist keine OS-VM.

## 0.2.304 — bisherige geprüfte Auslieferung
- ISAS-Verpackung, Windows-Neubau, 692 Core-/23 Connector-/19 Relay-/75 native UI-Prüfpunkte.
- MSI-Installation/Smoke/Deinstallation sowie produktiver Start mit erhaltenen acht Profilen und Projektzuordnungen.
- Privates Repository und vier zweisprachige README-Bereiche; Release-Tag bleibt unverändert.
