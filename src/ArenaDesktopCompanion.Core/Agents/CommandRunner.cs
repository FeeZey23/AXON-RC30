using System.Diagnostics;
using System.Text;

namespace ArenaDesktopCompanion.Core.Agents;

/// <summary>Ergebnis eines ausgeführten Befehls.</summary>
/// <param name="Id">Welcher Befehl (aus dem <see cref="CommandCatalog"/>).</param>
/// <param name="ExitCode">Rückgabewert; <c>-1</c>, wenn das Programm gar nicht startete.</param>
/// <param name="TimedOut">Wurde abgebrochen, weil die Zeit ablief?</param>
/// <param name="Duration">Wie lange es gedauert hat.</param>
/// <param name="Output">Ausgabe, gekürzt und von Schlüsseln befreit.</param>
/// <param name="Truncated">Wurde die Ausgabe gekürzt?</param>
/// <param name="Redacted">Stand etwas darin, das wie ein Schlüssel aussah?</param>
/// <param name="StartError">Meldung, falls das Programm nicht startete (z. B. nicht installiert).</param>
public sealed record CommandResult(
    string Id,
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    string Output,
    bool Truncated,
    bool Redacted,
    string? StartError = null)
{
    public bool Ok => ExitCode == 0 && !TimedOut && StartError is null;

    public string Zusammenfassung()
    {
        if (StartError is not null)
        {
            return $"{Id}: nicht ausführbar – {StartError}";
        }

        var kopf = TimedOut
            ? $"{Id}: Zeit abgelaufen nach {Duration.TotalSeconds:0} s"
            : $"{Id}: {(ExitCode == 0 ? "erfolgreich" : $"Rückgabewert {ExitCode}")} ({Duration.TotalSeconds:0.#} s)";
        return Truncated ? kopf + " · Ausgabe gekürzt" : kopf;
    }
}

/// <summary>
/// Führt einen Befehl aus der Liste aus und liefert seine Ausgabe zurück.
/// </summary>
/// <remarks>
/// <para>
/// Kein Shell-Aufruf, keine Zeichenkettenverkettung: Programm und Argumente gehen getrennt an
/// <c>ProcessStartInfo.ArgumentList</c>. Was hier aufgerufen wird, steht in
/// <see cref="CommandCatalog"/> und nirgends sonst.
/// </para>
/// <para>
/// Arbeitsordner ist immer der Projektordner – der Befehl kann also nichts außerhalb anfassen,
/// und der Nutzer sieht am Pfad, wo er wirkt.
/// </para>
/// </remarks>
public sealed class CommandRunner
{
    /// <summary>So viel Ausgabe wird behalten (Rest wird in der Mitte gekürzt).</summary>
    public const int MaxZeichen = 60_000;

    private readonly TimeSpan _standardTimeout;

    public CommandRunner(TimeSpan? standardTimeout = null) => _standardTimeout = standardTimeout ?? TimeSpan.FromMinutes(5);

    /// <summary>Läuft gerade ein Befehl? (Das Fenster erlaubt nur einen zur Zeit.)</summary>
    public bool Laeuft { get; private set; }

    /// <summary>
    /// Findet ein Programm auf diesem Rechner (Name → vollständiger Pfad) oder <c>null</c>.
    /// Das Fenster setzt das aus seiner Werkzeugsuche.
    /// </summary>
    public Func<string, string?>? ProgrammAufloesen { get; set; }

    /// <summary>
    /// Welches Programm wirklich gestartet wird. Ist das gesuchte nicht da, kommt das erste
    /// vorhandene Ersatzprogramm zum Zug – sonst bliebe der Eintrag in der Liste stehen, ohne je
    /// laufen zu können (auf dem Zielrechner fehlt <c>pwsh</c>, Windows PowerShell ist da).
    /// </summary>
    public static string WaehleProgramm(CommandSpec spec, Func<string, string?>? aufloesen)
    {
        if (aufloesen is null || spec.FileName.Contains(Path.DirectorySeparatorChar)
            || spec.FileName.Contains(Path.AltDirectorySeparatorChar) || spec.FileName.Contains('/'))
        {
            return spec.FileName;
        }

        var gefunden = aufloesen(spec.FileName);
        if (!string.IsNullOrWhiteSpace(gefunden))
        {
            return gefunden!;
        }

        foreach (var ersatz in spec.Ersatz)
        {
            var pfad = aufloesen(ersatz);
            if (!string.IsNullOrWhiteSpace(pfad))
            {
                return pfad!;
            }
        }

        // Nichts gefunden: den gewünschten Namen behalten, damit die Meldung ihn nennt.
        return spec.FileName;
    }

    /// <summary>
    /// Endungen, bei denen ein Argument eine <b>Datei im Projektordner</b> meint und keine Angabe.
    /// Nur solche Argumente werden vorab geprüft – <c>dotnet --version</c> oder <c>git status</c>
    /// haben nichts, was fehlen könnte.
    /// </summary>
    private static readonly string[] DateiArgumente =
        [".sln", ".slnx", ".csproj", ".ps1", ".psm1", ".psd1", ".cmd", ".bat", ".sh"];

    /// <summary>
    /// Fehlt etwas, das der Befehl braucht? Gibt die Klartextmeldung zurück oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Ein Befehl aus der Liste ist nur dann sinnvoll, wenn das Projekt, auf das er zeigt, auch da
    /// ist. Der Prüfstand setzt den Projektordner selbst, der Betrieb nimmt den, den der Nutzer
    /// gewählt hat – und der kann ein Update-Ordner sein, in dem nur ein Teil des Projekts liegt.
    /// </remarks>
    public static string? FehlendeVoraussetzung(string projectRoot, CommandSpec spec)
    {
        foreach (var argument in spec.Arguments)
        {
            if (!SiehtNachDateiAus(argument))
            {
                continue;
            }

            var voll = Path.GetFullPath(Path.Combine(projectRoot, argument.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(voll))
            {
                return $"In diesem Projektordner liegt das Projekt nicht ({argument} fehlt) – erwartet: {voll}";
            }
        }

        return null;
    }

    private static bool SiehtNachDateiAus(string argument)
        => !argument.Contains('*')
           && DateiArgumente.Any(endung => argument.EndsWith(endung, StringComparison.OrdinalIgnoreCase));

    public async Task<CommandResult> RunAsync(
        string projectRoot,
        CommandSpec spec,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var start = Process.GetCurrentProcess().StartTime; // nur, um die Uhr zu lesen
        var uhr = Stopwatch.StartNew();
        var grenze = timeout ?? (spec.Timeout > TimeSpan.Zero ? spec.Timeout : _standardTimeout);

        if (!Directory.Exists(projectRoot))
        {
            return new CommandResult(spec.Id, -1, false, uhr.Elapsed, string.Empty, false, false,
                $"Projektordner nicht gefunden: {projectRoot}");
        }

        // Befund vom 21.09.2026 (TODO 8.16): Lag im Projektordner kein vollständiges Projekt (nur
        // die Update-Teilmenge in einem Unterordner), startete »Selbsttest« trotzdem und endete mit
        // „Das Argument »scripts/selftest.ps1« für den -File-Parameter ist nicht vorhanden“ – eine
        // Meldung, die den eigentlichen Grund nicht nennt. Jetzt wird vorab geprüft.
        if (FehlendeVoraussetzung(projectRoot, spec) is { } fehlt)
        {
            return new CommandResult(spec.Id, -1, false, uhr.Elapsed, string.Empty, false, false, fehlt);
        }

        var info = new ProcessStartInfo
        {
            FileName = WaehleProgramm(spec, ProgrammAufloesen),
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Laeuft = true;
        try
        {
            using var prozess = new Process { StartInfo = info };
            var ausgabe = new StringBuilder();

            prozess.OutputDataReceived += (_, e) => Anhaengen(ausgabe, e.Data);
            prozess.ErrorDataReceived += (_, e) => Anhaengen(ausgabe, e.Data);

            try
            {
                if (!prozess.Start())
                {
                    return new CommandResult(spec.Id, -1, false, uhr.Elapsed, string.Empty, false, false,
                        "Prozess konnte nicht gestartet werden.");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                // Häufigster Fall: das Programm ist auf diesem Rechner nicht vorhanden.
                return new CommandResult(spec.Id, -1, false, uhr.Elapsed, string.Empty, false, false, ex.Message);
            }

            prozess.BeginOutputReadLine();
            prozess.BeginErrorReadLine();

            using var abbruch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            abbruch.CancelAfter(grenze);
            var zeitAbgelaufen = false;

            try
            {
                await prozess.WaitForExitAsync(abbruch.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                zeitAbgelaufen = !cancellationToken.IsCancellationRequested;
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                try
                {
                    prozess.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // Schon beendet oder nicht tötbar – der Abbruch steht trotzdem im Ergebnis.
                }
            }

            // Die asynchronen Leser brauchen einen Moment, bis die letzten Zeilen da sind.
            try
            {
                prozess.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
                // Prozess schon weg.
            }

            uhr.Stop();
            var roh = ausgabe.ToString();
            var redigiert = SecretRedaction.Clean(roh);
            var (gekuerzt, warGekuerzt) = Kuerzen(redigiert);

            // Befund vom 21.09.2026 (rc13, live): Läuft der Companion noch, sperrt er seine eigenen
            // Ausgabedateien – MSBuild bricht dann nach zehn Wiederholungen mit MSB3021/MSB3027 ab.
            // Die Meldung nennt zwar die Datei, aber nicht die Ursache. Hier steht sie dabei.
            if (ErklaerungFuerDateisperre(gekuerzt) is { } erklaerung)
            {
                gekuerzt = gekuerzt.TrimEnd() + Environment.NewLine + Environment.NewLine
                           + "Erklärung: " + erklaerung + Environment.NewLine;
            }

            var exitCode = zeitAbgelaufen ? -1 : SichereExitCode(prozess);

            return new CommandResult(
                spec.Id,
                exitCode,
                zeitAbgelaufen,
                uhr.Elapsed,
                gekuerzt,
                warGekuerzt,
                !string.Equals(roh, redigiert, StringComparison.Ordinal));
        }
        finally
        {
            Laeuft = false;
            _ = start;
        }
    }

    private static int SichereExitCode(Process prozess)
    {
        try
        {
            return prozess.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static void Anhaengen(StringBuilder ziel, string? zeile)
    {
        if (zeile is null)
        {
            return;
        }

        // Harte Obergrenze schon beim Sammeln: ein Befehl, der in einer Schleife ausgibt, soll
        // nicht den Arbeitsspeicher füllen, bevor er gekürzt wird.
        if (ziel.Length < MaxZeichen * 2)
        {
            ziel.AppendLine(zeile);
        }
    }

    /// <summary>
    /// Die häufigste Ursache eines gescheiterten Bauens in Worte fassen: Die Anwendung läuft noch
    /// und sperrt ihre eigenen Ausgabedateien. Gibt <c>null</c> zurück, wenn die Ausgabe nichts
    /// davon zeigt.
    /// </summary>
    /// <remarks>
    /// Befund vom 21.09.2026, live über die Brücke: <c>build</c> scheiterte mit MSB3027/MSB3021 und
    /// der Zeile „The process cannot access the file … because it is being used by another process.
    /// Die Datei wird durch »Arena.ai Desktop Companion (26580)« gesperrt." Wer das liest, sucht den
    /// Fehler im Projekt statt im eigenen laufenden Fenster.
    /// </remarks>
    public static string? ErklaerungFuerDateisperre(string ausgabe)
    {
        var gesperrt = ausgabe.Contains("MSB3027", StringComparison.Ordinal)
                       || ausgabe.Contains("MSB3021", StringComparison.Ordinal)
                       || ausgabe.Contains("being used by another process", StringComparison.Ordinal)
                       || ausgabe.Contains("gesperrt", StringComparison.Ordinal);

        return gesperrt
            ? "Mindestens eine Ausgabedatei ist von einem laufenden Programm belegt – in aller Regel vom "
              + "Companion selbst (sein Fenster hält die Anwendungsdatei offen). Erst das Programm schließen, "
              + "dann bauen. Bibliothek und Tests lassen sich auch bei laufendem Fenster bauen; nur die "
              + "Anwendung selbst braucht sie geschlossen."
            : null;
    }

    /// <summary>Kürzt in der Mitte: Anfang (Fehlerursache) und Ende (Ergebnis) bleiben erhalten.</summary>
    public static (string Text, bool Gekuerzt) Kuerzen(string text)
    {
        if (text.Length <= MaxZeichen)
        {
            return (text, false);
        }

        var kopf = MaxZeichen / 2;
        var fuss = MaxZeichen - kopf;
        var marke = $"\n… {text.Length - MaxZeichen:N0} Zeichen ausgelassen …\n";
        return (text[..kopf] + marke + text[^fuss..], true);
    }
}
