# Betrieb, Diagnose und sichere Wiederaufnahme

## Normaler Ablauf

Ein Tab gehört zu genau einem gewählten Projekt und besitzt seine eigene Browser-/Connectoridentität. Vor einem Dateiauftrag sichtbaren Tab und Projektpfad prüfen. Connect setzt einen Entwurf ein; der Nutzer sendet. Erst ein passender bestätigter Rundlauf gilt als frischer Kontakt.

Für ein unabhängiges zweites Projekt einen zweiten AXON-Tab verwenden. Das Umschalten zwischen vorhandenen AXON-Tabs soll deren Identitäten erhalten. Eine andere Arena-Chat-ID im selben Tab ist dagegen ein Kontextwechsel und kann in Fix4 die alte Paarung verwerfen.

## Zustandsinterpretation

- **Lokal bereit:** Host läuft, noch keine Aussage über einen Cloud-Client.
- **Lebenszeichen lesbar:** Relay stellt eine Datei bereit; der Host könnte trotzdem keine neuen Jobs verarbeiten.
- **Peer bestätigt:** Aktuelle Connect-/Projektbindung hat einen passenden Rundlauf beantwortet.
- **Kontakt alt:** Keine kürzlich bestätigte Aktion; nicht automatisch ein beendeter Dienst.
- **Bindung falsch:** Nicht einfach weiterschreiben oder auf ein unsicheres Protokoll wechseln.

Neue Clients unterscheiden `status` mit Rundlauf von `status --transport-only`. Alte bereits installierte Workspace-Clients können ein älteres Statusverhalten zeigen.

## HTTP-Fehler

| Fehler | Sichere Reaktion |
|---|---|
| 403 | Verweigerung korrekt dokumentieren; keine Authdaten raten; kein unbegrenztes Polling. |
| 404 | Genau Endpunkt und Datei prüfen; nicht pauschal lokale App für beendet erklären. |
| 429 | Rate reduzieren, begrenzt warten; kein Retry-Sturm. |
| 503 | Serverseitige Nichtverfügbarkeit vom Projektzustand unterscheiden. |
| 507 | Relay speichert nicht; nicht ohne Beleg Windowsplattenmangel behaupten. |
| Timeout | Job könnte bereits laufen; bekannte ID/Journal prüfen, nicht blind neu ausführen. |

Der reale 403-Ursprung ist nicht abschließend geklärt. Ein lokaler Mock zeigte nur die unpassende Wiederholung und Fehlermeldung. Der später beobachtete 507 ist ein eigener Fehler, kein Nachweis derselben Ursache.

## Verhalten bei unklarer Antwort

Eine fehlende Ergebnisdatei ist kein Beleg für fehlenden Seiteneffekt. Zuerst bekannte Jobkennung und vorhandenen Zustand prüfen. Ein Neustart darf das Journal nicht löschen, um einen Auftrag noch einmal auszuführen. Beschädigtes Journal privat sichern und betroffenen Tab untersuchen. Andere Tabs können unabhängig weiterarbeiten.

## Verlauf und dauerhafte Entfernung

Schließen archiviert Konfiguration. Wiederöffnen erhält die zugehörige Profilidentität. Dauerhaft entfernen ist eine separate bewusste Aktion. Ein Projektordner darf dabei nicht als Browserprofil behandelt und gelöscht werden.

## Lokale Diagnose sammeln

Relevanten Zeitbereich und betroffene Session festhalten. Vollständige Logs zuerst privat lesen. Für Weitergabe Secrets, Cookie-/Tokenwerte, Chat-UUIDs, Benutzernamen und Projektpfade entfernen. Ein Browserprofil oder die vollständige Settingsdatei ist kein angemessener öffentlicher Fehleranhang.

Hilfreiche Angaben: Produktbasis, tatsächlich verwendete Runtime, Operation, Statuscode, Zeitpunkt, ob alte Paarung vorhanden war, ob ein neuer Rundlauf gelang und ob die Dateioperation lokal überprüft wurde. Unbelegte „alles verbunden“-Aussagen vermeiden.

## Backup und Rückweg

Produktives RC30 normal schließen, bevor private Zustände kopiert werden. Eine Configsicherung allein ist keine vollständige Sicherung eines laufend beschriebenen Browserprofils. Projektdateien und AppData getrennt sichern. Installer-Rückweg zunächst isoliert prüfen; vorherige lauffähige Runtime behalten. rc26/rc27 werden für RC30-Wartung nicht beendet oder verändert.
