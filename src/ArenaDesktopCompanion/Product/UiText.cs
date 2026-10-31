using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace ArenaDesktopCompanion.Product;
public static class UiText
{
    private static readonly Dictionary<string,string> English=new(StringComparer.Ordinal)
    {
        ["Einstellungen"]="Settings",["Projektdateien"]="Project files",["Protokolle"]="Logs",["Info"]="Info",["Verlauf ▾"]="History ▾",["Neue unabhängige Sitzung"]="New independent workspace",
        ["Sprache"]="Language",["Deutsch"]="German",["Englisch"]="English",["PROJEKT DIESER SITZUNG"]="PROJECT FOR THIS WORKSPACE",["Noch nicht gewählt"]="Not selected",["Projekt auswählen …"]="Choose project …",
        ["Ordner auswählen"]="Choose folder",["GitHub-Link laden"]="Load GitHub link",["Automatisch erstellen"]="Create automatically",["Abbrechen"]="Cancel",["Übernehmen"]="Apply",["Schließen"]="Close",
        ["Projekt-Downloads automatisch übernehmen"]="Automatically import project downloads",["Downloads in Projekt-Inbox ablegen"]="Route downloads to project inbox",
        ["GitHub verbinden"]="Connect GitHub",["GitHub-Status prüfen"]="Check GitHub account",["Offizielles Repository"]="Official repository",["Update im Repository prüfen"]="Check repository for update",["Update"]="Update",
        ["Projekt als GitHub-Repository veröffentlichen"]="Publish project as GitHub repository",["Repositoryname"]="Repository name",["Beschreibung"]="Description",["Privates Repository"]="Private repository",["Ausgewählte Dateien veröffentlichen"]="Publish selected files",
        ["Anmeldung merken"]="Remember login",["Anmeldung für diesen Tab merken und eingeloggt bleiben?"]="Remember this tab's login and stay signed in?",
        ["Ja: privates Profil bleibt gespeichert. Nein: InPrivate-Sitzung ohne dauerhafte Anmeldung."]="Yes: retain the private browser profile. No: use an InPrivate session without a persistent login.",
        ["Berechtigungen"]="Permissions",["Nur lesen"]="Read only",["Projektdateien ändern"]="Change project files",["Lokale Programme ausführen"]="Run local programs",
        ["Sitzung wird vorbereitet"]="Preparing workspace",["Projekt noch nicht eingerichtet"]="Project not set up",["Projekt in Einstellungen auswählen"]="Choose a project in Settings",["Dateibestand wird ermittelt …"]="Scanning project files …",["Noch kein Kontakt"]="No contact yet",["Vorbereiten …"]="Preparing …",
        ["Explorer"]="Explorer",["Aktualisieren"]="Refresh",["Protokoll öffnen"]="Open log",["Protokollordner öffnen"]="Open log folder",["Projekt im Explorer"]="Open project in Explorer",["Workspace-Dateibaum öffnen"]="Open project tree",
        ["Bitte zuerst einen Projektordner auswählen."]="Please choose a project folder first.",["Kein neueres kompatibles Update vorhanden."]="No newer compatible update available.",
        ["GitHub-Freigabe läuft. Code auf der offiziellen GitHub-Seite bestätigen."]="GitHub authorization pending. Confirm the code on the official GitHub website.",
        ["GitHub-Upload: Dateiauswahl prüfen; Secrets und Profile niemals veröffentlichen."]="GitHub upload: review selected files; never publish secrets or profiles.",
        ["Version"]="Version",["Projekt öffnen"]="Open project",["Anmeldung geändert; gilt beim nächsten Öffnen dieses Tabs."]="Login preference changed; applies when this tab is next opened.",
        ["Laufende Aufträge zuerst abschließen lassen."]="Wait for running jobs to finish first.",
        ["Umbenennen …"]="Rename …",
        ["Tab umbenennen"]="Rename tab",
        ["Arena-Chatnamen automatisch übernehmen"]="Use Arena chat title automatically",
        ["Gruppe ändern …"]="Change group …",
        ["Gruppe (leer = ohne Gruppe)"]="Group (empty = no group)",
        ["Arena-Anmeldung übernehmen von …"]="Copy Arena login from …",
        ["Nur diese Sitzung schließen"]="Close only this workspace",
        ["Panel schließen"]="Close panel",
        ["Aus-/wieder andocken"]="Detach / dock panel",
        ["Wieder öffnen"]="Reopen",
        ["Dauerhaft entfernen …"]="Remove permanently …",
        ["Keine geschlossenen Tabs"]="No closed tabs",
        ["Dauerhaft entfernen – Projekt und Arena-Chat bleiben erhalten"]="Remove permanently — project and Arena chat are retained",
        ["0 Projektdateien · Ordner noch leer"]="0 project files · folder is empty",
        ["eigenes Browserprofil"]="independent browser profile",
        ["Sitzungen unabhängig"]="workspaces are independent",
        ["Chat-Kontakt"]="Chat contact",
        ["Relay erreichbar"]="Relay reachable",
        ["Chat unbestätigt"]="Chat unconfirmed",
        ["Dateiabgleich"]="File synchronization",
        ["Tab gestört"]="Workspace error",
        ["Eigene Sitzung"]="Independent workspace",
        ["Geschlossen"]="Closed",
        ["Projekt für"]="Project for",
        ["Tab dauerhaft entfernen"]="Remove tab permanently",
        ["Tab samt Projekt, Chat und Browserprofil wieder geöffnet."]="Workspace reopened with project, chat and browser profile.",
        ["Arena-Anmeldung übernommen; Seite wird neu geladen."]="Arena login copied; reloading the page.",
        ["Quelle hat keine Arena-Cookies; dort zuerst anmelden."]="Source has no Arena cookies; sign in there first.",
        ["Konflikte entscheiden"]="Resolve conflicts",
        ["Offene Konflikte"]="Pending conflicts",
        ["Meine Datei (unverändert)"]="My file (unchanged)",
        ["Arena-Version"]="Arena version",
        ["kein Unterschied"]="no difference",
        ["Vorlage öffnen"]="Open decision template",
        ["Meine behalten"]="Keep mine",
        ["Beide behalten"]="Keep both",
        ["Arena übernehmen"]="Use Arena version",
        ["Alle übernehmen"]="Apply all answers",
        ["kein Eintrag ausgewählt"]="no entry selected",
        ["decisions.json im Editor öffnen – auch für mehrere Dateien auf einmal"]="Open decisions.json in an editor — also for multiple files at once",
        ["Deine Datei bleibt, die Arena-Version wandert in den Konfliktordner"]="Your file stays; the Arena version moves to the conflict folder",
        ["Meine Datei bleibt, die Arena-Version kommt zusätzlich daneben (datei.arena.py)"]="My file stays; the Arena version is saved alongside it (file.arena.py)",
        ["Meine Version wird vorher in den Ordner Backup\\ gesichert"]="My version is backed up in Backup\\ first",
        ["Alle bereits beantworteten Anfragen aus der Vorlage umsetzen"]="Apply all already answered items from the decision template",
        ["Diese Dateien haben sich seit dem letzten Import verändert. Entscheide pro Datei – nichts wird überschrieben, ohne dass du es gesehen hast."]="These files changed since the last import. Decide per file — nothing is overwritten without your review.",
        ["Keine offenen Konflikte. Was die App nicht einfach überschreiben durfte, ist erledigt."]="No pending conflicts. All files requiring your decision have been handled.",
        ["Jeder Tab hat ein eigenes Profil. Neue Tabs können die Arena-Anmeldung einmalig übernehmen (Shift+Plus: ohne Übernahme). Sicherung vor Dateiänderungen bleibt aktiv. Keine rc27-Einstellungen werden übernommen."]="Every tab has its own profile. New tabs can copy the Arena login once (Shift+Plus: no copy). Backups remain mandatory before file changes. No rc27 settings are imported.",
        ["RC30 · unabhängig von rc27 · eigene Profile und Daten"]="RC30 · independent of rc27 · separate profiles and data",
    };
    static UiText()=>EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((sender,_)=>Apply((DependencyObject)sender)));
    public static string Translate(string value)
    {
        var en=App.Services.Settings.UiLanguage=="en";
        if(en&&English.TryGetValue(value,out var text))return text;
        if(!en){foreach(var p in English)if(p.Value==value)return p.Key;}
        return value;
    }
    public static void Apply(DependencyObject root)
    {
        if(root is FrameworkElement { Tag: "UserContent" })return;
        if(root is Window window)window.Title=Translate(window.Title);
        if(root is MenuItem item&&item.Header is string header)item.Header=Translate(header);
        if(root is GroupBox group&&group.Header is string groupHeader)group.Header=Translate(groupHeader);
        if(root is MenuItem menuItem)foreach(var child in menuItem.Items.OfType<DependencyObject>())Apply(child);
        if(root is ContextMenu menu)foreach(var child in menu.Items.OfType<DependencyObject>())Apply(child);
        if(root is TextBlock t&&!BindingOperations.IsDataBound(t,TextBlock.TextProperty))t.Text=Translate(t.Text);
        if(root is ContentControl c&&c.Content is string s&&!BindingOperations.IsDataBound(c,ContentControl.ContentProperty))c.Content=Translate(s);
        if(root is FrameworkElement f&&f.ToolTip is string tip)f.ToolTip=Translate(tip);
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)Apply(VisualTreeHelper.GetChild(root,i));
    }
}
