# Entwicklung und Beiträge

## Grundregeln

Nur RC30 ändern; rc26/rc27 sind keine automatischen Migrationsziele. Nutzerdaten und Browserprofile gehören nie ins Repository. Bestehende interne Datenidentität nicht ohne getestete Migration umbenennen. Branding ist keine Erlaubnis, ursprüngliche Lizenzhinweise zu entfernen.

## Änderungen prüfen

1. Kleinen nachvollziehbaren Änderungsumfang wählen.
2. Passende Regression ergänzen; keine Tests löschen, nur weil sie einen Fehler finden.
3. Dokumentation, echte Screenshots und schematische Grafiken klar auseinanderhalten.
4. Auf Windows `scripts/Build.ps1` ausführen.
5. Installeränderungen isoliert mit Install/Upgrade/Deinstall testen.
6. Keine unerprobten Funktionen als fertig oder live verifiziert beschreiben.

`Test-Repository.ps1` findet offensichtliche private Dateien, aber nicht jedes Secret im Freitext. Vor dem Commit Diff und Bildinhalt prüfen. Buildartefakte, lokale Logs und private Archive bleiben außerhalb der Quellhistorie.

## Quellstil und Kompatibilität

Nullable-/Analyzerwarnungen ernst nehmen; Debug und Release mit Warnungen als Fehler bauen. Async-UIcallbacks auf Lebensdauer, Besitzer, Projekt und Abbruch prüfen. Dateioperationen durch die bestehenden Sicherheits-/Backuphüllen führen. Journalzustände nicht durch stilles Löschen reparieren.

## CI und Kosten

Der Windowsworkflow startet nur über `workflow_dispatch`. Ein manueller Lauf verbraucht gegebenenfalls private Actions-Kontingente und ersetzt keine native Desktop-/Installationsabnahme. Kein automatisches Billing, keine API-Keys und kein Signiersecret werden eingerichtet.
