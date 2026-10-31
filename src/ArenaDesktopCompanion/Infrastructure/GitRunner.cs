using System.Diagnostics;
using System.IO;
using System.Text;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Infrastructure;

public sealed record GitResult(bool Success, int ExitCode, string Output);

/// <summary>
/// Dünner Prozess-Aufruf für <c>git init</c>. Die Entscheidung, WAS getan werden soll,
/// liegt im getesteten <see cref="ProjectHygiene"/>; hier wird nur ausgeführt und abgefangen,
/// damit ein fehlendes Git die App nicht zum Absturz bringt.
/// </summary>
public static class GitRunner
{
    public static async Task<GitResult> RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("git konnte nicht gestartet werden.");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var exit = process.ExitCode;
            var text = (stdout.ToString() + stderr.ToString()).Trim();
            return new GitResult(exit == 0, exit, text);
        }
        catch (Exception ex)
        {
            return new GitResult(false, -1, ex.Message);
        }
    }

    /// <summary>Probiert die Kandidaten aus <see cref="ProjectHygiene.GitInitCommandCandidates"/> der Reihe nach.</summary>
    public static async Task<GitResult> InitRepositoryAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        GitResult? last = null;
        foreach (var args in ProjectHygiene.GitInitCommandCandidates())
        {
            last = await RunAsync(projectRoot, string.Join(' ', args), cancellationToken).ConfigureAwait(false);
            if (last.Success)
            {
                return last;
            }
        }
        return last ?? new GitResult(false, -1, "git nicht verfügbar");
    }
}
