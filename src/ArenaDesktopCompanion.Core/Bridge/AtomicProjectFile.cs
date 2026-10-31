using System.Security.Cryptography;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Backup muss vor der atomaren Übernahme erfolgreich sein. Kein stilles Überschreiben.</summary>
public static class AtomicProjectFile
{
    private static readonly object Gate = new();

    public static void WriteInside(string root, string relative, byte[] content, string? backupPath)
    {
        lock (Gate)
        {
            if (!BridgeProtocol.SichererPfad(root, relative, out var error))
                throw new InvalidOperationException(error);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            byte[]? original = File.Exists(target) ? SHA256.HashData(File.ReadAllBytes(target)) : null;
            if (original is not null)
            {
                if (backupPath is null) throw new IOException("Erforderliche Sicherung fehlt – Schreiben abgebrochen.");
                var backupRelative = Path.GetRelativePath(root, backupPath);
                if (!BridgeProtocol.SichererPfad(root, backupRelative, out error))
                    throw new InvalidOperationException(error);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(target, backupPath, overwrite: false);
                if (!SHA256.HashData(File.ReadAllBytes(backupPath)).AsSpan().SequenceEqual(original))
                    throw new IOException("Sicherung und Ausgangsdatei unterscheiden sich – Schreiben abgebrochen.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".axon-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(content);
                    output.Flush(flushToDisk: true);
                }
                if (!BridgeProtocol.SichererPfad(root, relative, out error)) throw new InvalidOperationException(error);
                if (original is null)
                {
                    File.Move(temp, target, overwrite: false);
                }
                else
                {
                    if (!File.Exists(target) || !SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(original))
                        throw new IOException("Datei wurde zwischenzeitlich verändert – Schreiben abgebrochen.");
                    File.Move(temp, target, overwrite: true);
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static void ReplaceBytes(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(content); output.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
