# Sicherheit und vertrauliche Daten

## Wesentliche Grenze

Die Anwendung läuft mit den **Benutzerrechten** des angemeldeten Windowskontos. Projektpfad, Allowlist und Arbeitsverzeichnis sind keine OS-Sandbox. Besonders autorisierte Skripte/Programme können auf mehr als das Projekt zugreifen. `vorschlag` führt in diesem Stand vorgesehene Aufrufe mit automatischer Hostbestätigung aus, nicht zwingend nach einer weiteren menschlichen Rückfrage.

## Geheimnisse

Paarungsdaten, Capsuleinhalt, Settings, Sessions und Browsercookies vertraulich halten. Keine aktiven Kennwörter, vollständigen Briefkastenadressen, PFX-/PEM-Schlüssel oder Browserprofile in Git, öffentliche Fehlerberichte oder Release-Assets übernehmen. Auch ein privates Repository ist kein Secretmanager.

RC30 verwendet AES-256-GCM mit Kontextbindung. Das verhindert keine lokale Schadsoftware im Benutzerkonto und keine Verfügbarkeitssperre des Relayservers. Metadaten können sichtbar bleiben. Keine stille Herabstufung auf unverschlüsselten Legacytransport.

## Meldung eines Problems

Noch ist kein öffentlicher Issue-/Security-Advisory-Kanal eingerichtet. Sicherheitsprobleme zuerst vertraulich an den bestätigten Projekteigentümer melden. Nur anonymisierte Reproduktionsschritte liefern. Kein aktives Pairing zur Reproduktion veröffentlichen. Ein vermutetes geleaktes Geheimnis lokal ersetzen und betroffene Bindung kontrolliert erneuern.

## Installervertrauen

Hashintegrität, funktionale Tests und digitale Signaturen sind verschieden. Der Installer wurde auf Windows geprüft und produktiv installiert, ist aber weiterhin unsigniert. „ISAS“ als Herstellertext verifiziert keine Identität. Windows-Sicherheitswarnungen nicht automatisch abschalten; unbekannte/manipulierte Pakete nicht ausführen.

## Datenschutz vor Veröffentlichung

Ignore-Regeln und automatischer Privacy-Check sind nur Hilfen. Änderungen, Screenshots, Quellbeispiele und Git-Historie zusätzlich manuell prüfen. Bereits eingecheckte Secrets werden durch spätere `.gitignore`-Regeln nicht entfernt.

## Experimenteller DOM-Beobachter

Die übernommenen Quellen enthalten `Assets/arena-bot.js` und einen Installer dafür. Dieser experimentelle Beobachter kann DOM-Inhalte/Codekandidaten erkennen. Er ist standardmäßig aus und nicht als offiziell genehmigter Arena-Zugriff zu verstehen. Keine Aktivierung oder Nutzung ohne passende Anbietererlaubnis. Der manuelle Download-/Connectweg ist von dieser Beobachterfunktion getrennt. Opt-in allein ersetzt keine Plattformgenehmigung.

## GitHub, Updates und Projekte ab 0.2.305

Offizieller Updatepfad ist ausschließlich `FeeZey23/AXON-RC30`; private Releases benötigen die lokale offizielle CLI-Anmeldung. Manifest/ProductId/Versionsformat/Assetname und GitHub-Asset-Digest werden geprüft, der heruntergeladene MSI-enthaltende Burn-Bootstrapper wird vor Aufruf erneut gehasht (auch nach Wartezeit auf das alte Programm). Dies schützt nicht vor einer kompromittierten Repository-Eigentümerschaft und ersetzt keine Signatur. Keine automatische Installation allein beim Prüfen.

Upload-Assistent: privater Standard, explizite Dateiauswahl, Prüfung und Abschlussbestätigung; ursprüngliches Projekt-Git bleibt unberührt. Scanner ersetzen keine persönliche Inhaltsprüfung. Projekt-Klonen startet keinen heruntergeladenen Code. Automatische Projektverzeichnisse bleiben bei Deinstallation als Nutzerdaten erhalten. Neue Tabs sind standardmäßig nur lesend; bestehende Freigaben bleiben kompatibel.

Externe WebView-Fenster zeigen die Adresse; erlaubte Hauptnavigation prüft exakten HTTPS-Host statt Zeichenkettenpräfix. OAuth-Opener/Profil werden erhalten; Anbieterbeschränkungen werden nicht umgangen. InPrivate ist Browser-Anmeldepersistenz, keine OS-Isolation oder Zusicherung, dass keinerlei Betriebssystemspuren entstehen.
