using System.IO.Compression;

namespace ArenaDesktopCompanion.Core.Sync;

public sealed record ArchiveImportReport(
    string ArchivePath,
    int Entries,
    int Written,
    int Updated,
    int Unchanged,
    int Conflicts,
    int Skipped,
    int Blocked,
    int Failed,
    IReadOnlyList<string> BlockNotes)
{
    public bool HadChanges => Written + Updated + Conflicts > 0;

    public string Summarize()
        => $"{Entries} Einträge · {Written} neu · {Updated} aktualisiert · {Unchanged} unverändert · " +
           $"{Conflicts} Konflikte · {Skipped} übersprungen · {Blocked} blockiert · {Failed} Fehler";
}

/// <summary>
/// Entpackt einen Arena-Workspace-zip in den Projektordner – Eintrag für Eintrag und mit
/// bewusster Ablehnung statt Abbruch: <see cref="ZipFile.ExtractToDirectory(string,string)"/>
/// wirft bei einem einzigen "../"-Eintrag den gesamten Import weg (verifiziert), und
/// <c>Path.Combine(root, entry.FullName)</c> löst ohne Prüfung aus dem Ordner heraus.
/// </summary>
public static class ArchiveImporter
{
    public static ArchiveImportReport ImportZip(string zipPath, SyncPolicy policy)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException)
        {
            return new ArchiveImportReport(zipPath, 0, 0, 0, 0, 0, 0, 0, 1,
                new[] { "Datei ist kein gültiges Zip-Archiv." });
        }
        catch (Exception ex)
        {
            return new ArchiveImportReport(zipPath, 0, 0, 0, 0, 0, 0, 0, 1,
                new[] { $"Archiv nicht lesbar: {ex.Message}" });
        }

        using (archive)
        {
            return ImportZip(archive, zipPath, policy);
        }
    }

    public static ArchiveImportReport ImportZip(ZipArchive archive, string displayName, SyncPolicy policy)
    {
        int total = 0, written = 0, updated = 0, unchanged = 0,
            conflicts = 0, skipped = 0, blocked = 0, failed = 0;
        var notes = new List<string>();

        var fileEntries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/').TrimStart('/')))
            .Where(x => x.Name.Length > 0)
            .ToList();

        // Top-Ordner nur entfernen, wenn ALLES darin liegt – sonst würden Projektdateien
        // versehentlich verschachtelt überschrieben.
        var commonRoot = policy.StripCommonArchiveRoot
            ? ArchiveLayout.DetectCommonRoot(fileEntries.Select(x => x.Name), policy.Root)
            : null;

        foreach (var (entry, nameBeforeStrip) in fileEntries)
        {
            var rawName = ArchiveLayout.RemoveRoot(nameBeforeStrip, commonRoot);

            total++;

            if (WorkspaceFileFilter.ShouldSkip(rawName))
            {
                skipped++;
                continue;
            }

            if (ArchiveLayout.LooksAbsolute(nameBeforeStrip))
            {
                blocked++;
                if (notes.Count < 20)
                {
                    notes.Add($"blockiert '{nameBeforeStrip}': absoluter Pfad im Archiv.");
                }
                continue;
            }

            if (!PathSafety.TryResolveInside(policy.Root, rawName, out var relative, out _, out var pathError))
            {
                blocked++;
                if (notes.Count < 20)
                {
                    notes.Add($"blockiert '{rawName}': {pathError}");
                }
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = ReadWithGuards(entry, policy);
            }
            catch (ZipBombException ex)
            {
                blocked++;
                notes.Add($"blockiert '{relative}': {ex.Message}");
                continue;
            }
            catch (Exception ex)
            {
                failed++;
                notes.Add($"Fehler '{relative}': {ex.Message}");
                continue;
            }

            var result = policy.Apply(relative, bytes, "workspace.zip");
            switch (result.Outcome)
            {
                case SyncOutcome.Written: written++; break;
                case SyncOutcome.UpdatedWithBackup: updated++; break;
                case SyncOutcome.Unchanged: unchanged++; break;
                case SyncOutcome.ConflictPreserved:
                    conflicts++;
                    notes.Add($"Konflikt '{relative}' → {result.ConflictRelativePath}");
                    break;
                case SyncOutcome.Blocked:
                    blocked++;
                    notes.Add($"blockiert '{relative}': {result.Message}");
                    break;
                case SyncOutcome.Skipped: skipped++; break;
                case SyncOutcome.Failed:
                    failed++;
                    notes.Add($"Fehler '{relative}': {result.Message}");
                    break;
            }
        }

        return new ArchiveImportReport(displayName, total, written, updated, unchanged,
            conflicts, skipped, blocked, failed, notes);
    }

    /// <summary>Liest einen Eintrag mit Größen- und Ratio-Grenzen (Zip-Bomben, Riesen-Dateien).</summary>
    private static byte[] ReadWithGuards(ZipArchiveEntry entry, SyncPolicy policy)
    {
        var maxBytes = policy.MaxFileBytes;
        using var stream = entry.Open();
        using var buffer = new MemoryStream((int)Math.Min(Math.Max(entry.Length, 0), 1L << 20));
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new ZipBombException($"Datei im Archiv größer als Limit {maxBytes} Bytes.");
            }
            buffer.Write(chunk, 0, read);

            var compressed = entry.CompressedLength;
            // Kein Größen-Untergrenze: kleine, extrem komprimierbare Einträge sind das
            // klassische Bombenmuster. Ratio > 200 ist bei Quelltext nie normal.
            if (compressed > 0 && total > Math.Max(policy.MaxCompressionRatio * compressed, 1L << 20))
            {
                throw new ZipBombException($"Unverhältnis entpackt/gepackt > {policy.MaxCompressionRatio}:1 (Zip-Bomben-Muster).");
            }
        }

        return buffer.ToArray();
    }

    private sealed class ZipBombException : Exception
    {
        public ZipBombException(string message) : base(message) { }
    }
}
