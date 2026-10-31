namespace ArenaDesktopCompanion.Core.Sync;

/// <summary>
/// Vorversionen vor dem Überschreiben, als Zeitstempel-Snapshots unter <c>Backup/</c>.
/// Nur die letzten <c>runsToKeep</c> Stände bleiben übrig.
/// </summary>
public static class BackupRotator
{
    /// <returns>relativer Pfad der gesicherten Vorversion, oder <see langword="null"/> wenn nichts gesichert wurde.</returns>
    public static string? Create(string root, string relativePath, int runsToKeep = 20)
    {
        var source = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source))
        {
            return null;
        }

        // Ein Lauf = ein Zeitstempel-Ordner; mehrere Dateien desselben Imports landen darin.
        var runDir = Path.Combine(root, SyncPolicy.BackupDirName, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var target = Path.Combine(runDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            Prune(root, runsToKeep);
            return Path.GetRelativePath(root, target).Replace('\\', '/');
        }
        catch (Exception)
        {
            // Backup darf den Sync nicht kippen; der Aufrufer meldet es höchstens im Log.
            return null;
        }
    }

    public static void Prune(string root, int runsToKeep)
    {
        if (runsToKeep < 0)
        {
            runsToKeep = 0;
        }

        var backupRoot = Path.Combine(root, SyncPolicy.BackupDirName);
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var runs = Directory.GetDirectories(backupRoot)
            .Select(d => (Path: d, Schluessel: Sortierschluessel(Path.GetFileName(d))))
            .OrderByDescending(x => x.Schluessel, StringComparer.Ordinal) // Zeitstempel-Format ist sortierfähig
            .Skip(runsToKeep)
            .ToList();

        foreach (var run in runs)
        {
            try
            {
                Directory.Delete(run.Path, recursive: true);
            }
            catch (Exception)
            {
                // löschen ist best-effort
            }
        }
    }
    /// <summary>
    /// Sortierschlüssel eines Sicherungsordners. Die Brücke legt ihre Sicherungen als
    /// <c>bruecke-&lt;zeit&gt;</c> an; ohne diesen Griff stünde das „b“ vor jeder Ziffer, die
    /// Brücken-Sicherungen galten damit immer als die neuesten – und die Rotation hätte die
    /// falschen Stände gelöscht (Fund aus dem Master-Check, 24.09.2026).
    /// </summary>
    internal static string Sortierschluessel(string ordnerName)
        => ordnerName.StartsWith("bruecke-", StringComparison.Ordinal)
            ? ordnerName["bruecke-".Length..]
            : ordnerName;
}
