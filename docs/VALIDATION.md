# Prüfnachweise und offene Gates

Stand: 31.10.2026. **Windows-Neubau, MSI-Prüfung und produktive Installation erfolgreich.** Repository und Release sind privat veröffentlicht; Sichtbarkeit, Commit und Asset-Hashes wurden geprüft. [Maschinenlesbare anonymisierte Nachweise](RELEASE.EVIDENCE.json).

## Tatsächlich ausgeführter neuer Windowslauf

| Ebene | Ergebnis |
|---|---:|
| Debug und Release | Erzwungener Neubau, 0 Fehler / 0 Warnungen |
| Core | 692 bestanden, 0 Fehler / 0 übersprungen |
| C#↔Node | 23 bestanden |
| Relay/Redirect | 19 bestanden |
| Native UI | 75 bestanden; reale MouseDown/Move/Drop-Ereignisse; 0 Skip |
| MSI | Gebaut, Version 0.2.304, Self-contained Windows x64 |
| Separates Installationsziel | Installation, installierte Smoke-UI und Deinstallation bestanden |
| OS-VM | Nicht vorhanden / nicht behauptet |
| Produktive Installation | Erfolgreich, sichtbares ISAS-Fenster bestätigt |
| Nutzerdaten | Acht Profile und vorhandene Projektzuordnungen erhalten |
| Ältere Companion-Instanz | Erhalten |
| Signierung | Nicht signiert |

Die 76 Einträge der installierten Smoke-Ausgabe bestehen aus 75 Prüfpunkten und einer Hardwareinformationszeile. Keine zusätzlichen Tests werden daraus erfunden. Ein vollständiger neue Quellenhash und der exakte MSI-Hash sind im JSON-Nachweis enthalten. Nachträglich aktualisierte Dokumentation wird als solche gekennzeichnet und separat durch die Core-/Dokumentationsverträge geprüft; das geprüfte MSI und die Produktquellen werden dabei nicht verändert.

## Releasegates

- [x] Bestätigter Windowsprojektpfad und Bestandsaufnahme.
- [x] Gehashte private Sicherung vor Bereinigung.
- [x] Vollständiger Visual-Studio-Quellordner auf dem PC.
- [x] Debug/Release ohne inkrementelle Alt-DLLs.
- [x] Core, Connector und Relayregressionen.
- [x] Native UI ohne übersprungene Geste.
- [x] Self-contained-Publish und WiX-MSI.
- [x] Getrenntes MSI-Installationsziel, installierte Smoke-UI und Deinstallation.
- [x] Produktive lokale Installation mit Fenster-/Profil-/Projektprüfung.
- [ ] Vollständige OS-VM-/Upgrade-/Reparaturmatrix.
- [ ] Reale Mixed-DPI-/Monitor-Abziehmatrix.
- [ ] Authenticode-Signierweg, falls gewünscht.
- [x] Autorisierte private GitHub-Veröffentlichung.

## Während der Vorbereitung gefundene und nicht versteckte Fehler

Der Privacy-Check unterschied zunächst nicht zwischen echten PEMdaten und kurzen Test-/Redaktionsmarkern; er prüft jetzt tatsächlichen Schlüsselblockinhalt. WiX verlangte explizite stabile GUIDs für Komponenten mit Datei und HKCU-Keypath. Der MSI-Zielpfad musste für korrekte Deinstallation gespeichert werden. ZIP-Zeitstempel konnten einen inkrementellen Build alter DLLs begünstigen: Debug/Release werden jetzt neu gebaut, und vor RID-Clean/Publish werden die passenden RID-Assets restauriert. Der native Test verlangt nicht nur Indexvergleich, sondern echte Eingabe-/Drop-Ereignisse. Dafür wurde der Besitzer-geprüfte Preview-Dragpfad in DockCard korrigiert. Frühere Fehl-/Skip-/inkrementelle Vorbereitungsläufe werden nicht nachträglich als neue vollständige Freigabe ausgegeben.

## Separat offene Produktakzeptanz

Reale Relay-403-/507-Ursachen, bessere Fehlerklassifikation/Wiederaufnahme, verständliche Inaktivitätsanzeige, sichere Rückkehr nach Chatnavigation sowie vollständige in-flight-/Negativnachweise bleiben offen. Loopback-/Regressions-/Installererfolg ist keine Verfügbarkeitsgarantie für einen öffentlichen Relayserver. Kein absoluter 100-%-Anspruch.
