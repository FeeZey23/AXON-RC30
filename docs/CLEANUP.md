# Bereinigung und Herkunft

Diese bereinigte Quellfassung basiert auf RC30 Fix4. Enthalten sind aktuelle Anwendungsquellen, notwendige automatisierte Tests und neu gepflegte Dokumentation/Installerquellen. Nicht enthalten sind alte Release-ZIPs, verworfene Versionen, persönliche Logs, Chatverläufe, Maschinenkonfigurationen, Zugangsdaten oder einmalige Diagnose-/Aktivierungsskripte.

Auf der Agent-Arbeitsfläche wurden alte RC30-Hilfsdateien erst nach SHA-256-geprüfter privater Sicherung entfernt. Diese Sicherung liegt **außerhalb** dieses Git-Projekts. Der alte getestete Quellstand und das letzte geprüfte Fix4-Paket bleiben als technische Rückfallbasis außerhalb des Repositories erhalten. rc26/rc27-Originaldateien und private Nutzerdaten wurden nicht gelöscht.

**Windows-Bereinigung abgeschlossen:** 8.654 Dateien wurden vor dem Entfernen von 35 alten Einträgen privat gesichert und pro ZIP-Eintrag per SHA-256 geprüft. Aktive und vorherige Runtime blieben erhalten. 283 geschützte Originaldateien waren unverändert. Die ältere Companion-Instanz blieb während der abschließenden Installation erhalten. Acht bestehende RC30-Browserprofile und vorhandene Projektzuordnungen wurden nach der MSI-Installation bestätigt.

Reale Benutzernamen/Rechnernamen in Testfixtures wurden durch Beispieldaten ersetzt; reale Chat-IDs durch künstliche UUIDs. Programm-Namespaces und persistente RC30-Datenpfade bleiben absichtlich stabil. Einmalige Testscripte wurden nicht übernommen; produktnotwendige Unit-, Integrations- und UI-Regressionstests bleiben erhalten.

Die vorhandene MIT-Lizenz mit ursprünglichem Copyright-Vermerk wird beibehalten. ISAS ist die gewünschte Produktbezeichnung, keine behauptete Zertifikatsidentität und keine offizielle Arena-Partnerschaft.
