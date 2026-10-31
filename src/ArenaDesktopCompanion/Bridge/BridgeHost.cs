using System.Text.Json;
using System.IO;
using System.Text;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Diagnostics;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion.Bridge;

/// <summary>
/// Was die Brücke über den Rechner wissen muss, um ein Abbild zu bauen. Das Fenster kennt den
/// Projektordner, den Sync-Zustand und das Protokoll – der Host holt es sich hier ab, statt die
/// halbe Anwendung zu kennen.
/// </summary>
public sealed record BridgeUmgebung(
    string? ProjektOrdner,
    GitState? Git,
    string? GitStat,
    IReadOnlyDictionary<string, int>? Inbox,
    IReadOnlyList<string>? Protokoll);

/// <summary>
/// Hält die Brücke am Leben: ein Hintergrund-Takt, der Abbilder schickt, Aufträge abholt und
/// Ergebnisse zurücklegt. Seit rc24 gibt es <b>einen Host je Reiter</b> – deshalb läuft der Chat
/// im Hintergrund weiter, während vorn ein anderer Reiter offen ist.
/// </summary>
/// <remarks>
/// Bewusst getrennt vom Fenster: Der Kern (<see cref="BridgeService"/>) ist ohne Oberfläche
/// prüfbar, und hier drin steht nur, was die App zusätzlich beisteuert – Projektordner, Werkzeuge,
/// Protokollzeilen, Sicherungskopien. Fällt das Netz aus, läuft das Fenster weiter; der Fehler
/// steht im Status und im Protokoll.
/// </remarks>
public sealed class BridgeHost : IDisposable
{
    private readonly AppServices _services;

    /// <summary>
    /// Der Reiter, zu dem diese Brücke gehört (seit rc24 ein Host je Reiter). Ist er <c>null</c>,
    /// gelten die flachen Einstellungen – so bleibt der Ein-Reiter-Fall und der Prüfstand einfach.
    /// </summary>
    private readonly Arbeitsbereich? _bereich;
    private readonly RelayClient _relay = new();
    private readonly CommandRunner _runner = new();
    private readonly Dictionary<string, string> _werkzeuge = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hash-Gedächtnis des Ordner-Abbilds: nur geänderte Dateien werden gerechnet. Ohne das kostete
    /// ein großer Projektordner (Zehntausende Dateien) bei jedem Takt Minuten – siehe
    /// <see cref="HashCache"/>.
    /// </summary>
    private readonly HashCache _hashCache = new();
    private readonly object _manifestGate = new();
    private CancellationTokenSource? _abbruch;
    private Task? _lauf;
    private string? _gebundenerOrdner;
    public string? GebundenerOrdner => _gebundenerOrdner;
    public bool SichererTransport => _bereich?.BridgeEncrypted == true;
    private bool _entsorgt;
    private bool _restoredPairing;

    /// <summary>Weckt den sitzungseigenen Takt bei neuer Connect-Anforderung.</summary>
    private readonly SemaphoreSlim _wecken = new(0);

    public BridgeHost(AppServices services, Arbeitsbereich? bereich = null)
    {
        _services = services;
        _bereich = bereich;
        ErmittleWerkzeuge();

        // Der Ausführer fragt hier nach, welches Programm es wirklich gibt. So läuft »Selbsttest«
        // auch auf Rechnern ohne pwsh: dann kommt Windows PowerShell zum Zug (Befund vom 21.09.2026).
        _runner.ProgrammAufloesen = name =>
            _werkzeuge.TryGetValue(name, out var pfad) && pfad.Length > 0 ? pfad : null;
    }

    /// <summary>
    /// Merkliste erledigter Aufträge – neben settings.json, also außerhalb des Projektordners.
    /// Sie ist der Grund, warum ein Neustart des Companions keinen Auftrag wiederholt.
    /// </summary>
    public string ErledigtDatei => Path.Combine(Merkordner, "bridge-erledigt" + MerkAnhang + ".txt");

    /// <summary>
    /// Der zuletzt verschickte Stand des Projektordners. Damit weiß die Brücke nach einem Neustart
    /// der App, was sich in der Zwischenzeit geändert hat – statt ein volles Abbild nachzuschicken
    /// oder die Änderungen zu verschweigen.
    /// </summary>
    public string StandDatei => Path.Combine(Merkordner, "bridge-stand" + MerkAnhang + ".json");

    /// <summary>Neben settings.json, also außerhalb jedes Projektordners.</summary>
    private string Merkordner => _bereich is null ? Rc30Paths.DataRoot : Rc30Paths.SessionFolder(_bereich.Id);

    /// <summary>
    /// Anhang für die Merkdateien: je Briefkasten einer. Ohne das würde der zweite Reiter die
    /// Aufträge des ersten für „schon erledigt“ halten – die Merkliste war bis rc23 eine einzige.
    /// </summary>
    private string MerkAnhang
    {
        get
        {
            var name = Bin;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var sauber = new string(name.Trim().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
            return sauber.Length == 0 ? string.Empty : "-" + sauber;
        }
    }

    /// <summary>Der Briefkasten dieses Reiters (Reiter-Bindung schlägt Einstellungen).</summary>
    private string? Bin => _bereich?.BridgeBin ?? _services.Settings.BridgeBin;

    /// <summary>Das Kennwort dazu.</summary>
    private string? Token => _bereich?.BridgeToken ?? _services.Settings.BridgeToken;

    /// <summary>Der eingestellte Projektordner dieses Reiters (null = noch keiner gewählt).</summary>
    private string? OrdnerEinstellung => _bereich?.ProjectFolder ?? _services.Settings.ProjectFolder;

    /// <summary>Liefert den Zustand des Rechners für das Abbild (vom Fenster gesetzt).</summary>
    public Func<CancellationToken, Task<BridgeUmgebung>>? Umgebung { get; set; }

    /// <summary>Wohin Meldungen gehen (Protokoll im Fenster).</summary>
    public Action<string>? Meldung { get; set; }

    /// <summary>Wird nach jedem Takt gerufen – das Fenster frischt dann seine Statuszeile auf.</summary>
    public Action? ZustandGeaendert { get; set; }

    /// <summary>Der laufende Dienst – oder <c>null</c>, wenn die Brücke aus ist.</summary>
    public BridgeService? Dienst { get; private set; }

    public bool Laeuft => _abbruch is not null && _lauf is { IsCompleted: false };

    public IReadOnlyList<BridgeRunInfo> Verlauf => Dienst?.Verlauf ?? [];

    /// <summary>
    /// Eine Zeile für die Oberfläche: ob sie läuft, wann zuletzt Kontakt bestand und was
    /// schiefgeht. Genau das, was man beim Blick auf den Bildschirm wissen will.
    /// </summary>
    public string Status
    {
        get
        {
            if (!RelayClient.NameIstGueltig(Bin))
            {
                return "nicht eingerichtet – Briefkasten fehlt";
            }

            if (string.IsNullOrWhiteSpace(Token))
            {
                return "nicht eingerichtet – Kennwort fehlt";
            }

            if (!Laeuft)
            {
                return "angehalten";
            }

            if (Dienst?.LetzterFehler is { Length: > 0 } fehler)
            {
                return "gestört: " + Kurz(fehler);
            }

            if(Dienst?.PeerRestored==true)return "Sitzung wiederhergestellt · wartet auf frischen Chatkontakt";
            if(_restoredPairing&&Dienst?.PeerConfirmed!=true)return "Gespeicherte Paarung · nächste Chataktion verbindet automatisch";
            if(string.IsNullOrWhiteSpace(_bereich?.ConnectionId))return "Kein Chat an diesen Ordner gebunden · Connect erforderlich";
            if(Dienst?.PeerConfirmed==true&&Dienst.PeerLastSeen<=DateTimeOffset.UtcNow.AddMinutes(-3))return "Chat-Bestätigung älter als 3 Min · neue Aktion/Connect nötig";
            if (Dienst?.PeerConfirmed == true && Dienst.PeerLastSeen > DateTimeOffset.UtcNow.AddMinutes(-3)) return "Chat verbunden · AES-GCM";
            return Dienst?.LetzterKontakt is not null ? "Transport bereit · Chat noch nicht bestätigt" : "Verbindung wird vorbereitet …";
        }
    }

    /// <summary>Wie viele Aufträge bisher ausgeführt wurden – für die Statuszeile.</summary>
    public int Auftraege => Dienst?.Verlauf.Count ?? 0;

    /// <summary>Startet den Takt, wenn die Einstellungen es hergeben. Sonst passiert nichts.</summary>
    public void Start()
    {
        Stop();

        var s = _services.Settings;
        if (!RelayClient.NameIstGueltig(Bin) || string.IsNullOrWhiteSpace(Token))
        {
            Dienst = null;
            return;
        }

        _gebundenerOrdner = string.IsNullOrWhiteSpace(OrdnerEinstellung) ? null : Path.GetFullPath(OrdnerEinstellung);
        var secret=Token!.Trim();
        _relay.Bin = Bin!.Trim();
        _relay.TransportSecret = SichererTransport ? secret : null;
        var interval = Math.Clamp(s.BridgeIntervalSeconds, 3, 120);

        Dienst = new BridgeService(
            _relay,
            _runner,
            Lesen,
            Schreiben,
            () => secret,
            AbbildErzeugen,
            meldung =>
            {
                Meldung?.Invoke(meldung);
                _services.Logger.Write("INFO", meldung);
            },
            gedaechtnis: new DateiGedaechtnis(ErledigtDatei),
            standSpeicher: new DateiStand(StandDatei),
            freigeben: Freigeben,
            dateiSchreibenBytes: SchreibenBytes)
        {
            // Bewusst eine Funktion: der Projektordner wird bei jedem Zugriff frisch geholt. Beim
            // Start kennt das Fenster ihn noch nicht – ein einmal übernommener Wert wäre dauerhaft
            // leer, und jeder Auftrag endete mit »Projektordner nicht gefunden« (Fehler bis rc7).
            ProjektOrdner = ProjektOrdnerJetzt,
            Journal = new BridgeJobJournal(ErledigtDatei + ".journal.json"),

            // Authentifizierung, feste Befehlsliste, Pfadschutz und Journal bleiben aktiv.
            RequirePeerConfirmation = true,
            BackgroundCommands = true,
            BefehleErlaubt = _bereich?.AccessProfile is null or "full",
            SchreibendErlaubt = _bereich?.AccessProfile is null or "write" or "full",
            VorschlaegeErlaubt = _bereich?.AccessProfile is null or "full",
            AbbildAbstand = TimeSpan.FromSeconds(20),
            HelloAbstand = TimeSpan.FromSeconds(20),
        };
        Dienst.ConnectionId = _bereich?.ConnectionId ?? "";
        _restoredPairing=!string.IsNullOrWhiteSpace(_bereich?.ConnectionId);
        if(_bereich is{} bound)
        {
            var service=Dienst;var root=bound.ProjectFolder;var bin=bound.BridgeBin;var resumeSecret=bound.BridgeToken;
            var proofPath=Path.Combine(Rc30Paths.SessionFolder(bound.Id),"peer-binding.json");
            if(PeerResumeState.Read(proofPath,bound) is{} proof&&service.RestoreConfirmedPeer(bound.ConnectionId,proof.LastSeen))Meldung?.Invoke("Bestätigte Sitzung wiederhergestellt; wartet auf frischen Chatkontakt");
            service.AcceptedJob+=_=>
            {
                if(service.ConfirmedPeerSnapshot() is not{} peer||bound.ProjectFolder!=root||bound.BridgeBin!=bin||bound.BridgeToken!=resumeSecret||bound.ConnectionId!=peer.Id)return;
                var snapshot=new Arbeitsbereich{Id=bound.Id,ProjectFolder=root,BridgeBin=bin,BridgeToken=resumeSecret,ConnectionId=peer.Id,ConnectedChatAddress=bound.ConnectedChatAddress};
                try{var proof=PeerResumeState.Create(snapshot,peer.Seen);Directory.CreateDirectory(Path.GetDirectoryName(proofPath)!);AtomicProjectFile.ReplaceBytes(proofPath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(proof)));}
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){Meldung?.Invoke("Sitzungsbestätigung nicht gespeichert: "+ex.Message);}
            };
        }

        _abbruch = new CancellationTokenSource();
        var token = _abbruch.Token;
        _lauf = Task.Run(() => LaufAsync(token, interval), CancellationToken.None);
        _services.Logger.Write("INFO",
            $"Brücke gestartet: Briefkasten {Anzeige(_relay.Bin)} · alle Rechte frei · Skripte {SkriptEngine()} · Takt {interval}s");
    }

    /// <summary>Die Schleife sofort weiterlaufen lassen (statt bis zum nächsten Takt zu warten).</summary>
    public void Wecken()
    {
        try
        {
            if (_wecken.CurrentCount == 0)
            {
                _wecken.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Fenster schließt gerade – dann ist nichts mehr zu wecken.
        }
    }

    /// <summary>Warten bis zum nächsten Takt – oder bis jemand weckt.</summary>
    private DateTimeOffset _fastUntil;
    public void PrepareConnect(string id)
    {
        _restoredPairing=false;
        if(Dienst is not null) Dienst.ConnectionId=id;
        _fastUntil=DateTimeOffset.UtcNow.AddSeconds(45);Wecken();
    }
    private async Task WartenAsync(int interval, CancellationToken cancellationToken)
    {
        await _wecken.WaitAsync(TimeSpan.FromSeconds(DateTimeOffset.UtcNow < _fastUntil ? 1 : interval), cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        var cts = _abbruch;
        _abbruch = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { /* schon weg */ }
            var loop=_lauf;var service=Dienst;
            _=Task.Run(async()=>
            {
                try{if(loop is not null)await loop.ConfigureAwait(false);if(service is not null)await service.WaitForPublicationAsync().ConfigureAwait(false);}
                finally{cts.Dispose();}
            });
        }

        // Task behalten, damit Dispose auf das Ende des laufenden Takts warten kann.
    }

    private async Task LaufAsync(CancellationToken cancellationToken, int interval)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Jede Sitzung hat ihren eigenen Dienst und Lebenszyklus.
                if (Dienst is null)
                {
                    ZustandGeaendert?.Invoke();
                    await WartenAsync(interval, cancellationToken).ConfigureAwait(false);
                    continue;
                }


                // Erst den Rechnerzustand holen (Git, Protokoll, Inbox), dann handeln.
                await AktualisiereUmgebungAsync(cancellationToken).ConfigureAwait(false);

                // Einmal warnen, nicht bei jedem Takt: ohne Projektordner kann die Brücke nichts
                // ausführen – das gehört sichtbar ins Protokoll, nicht in die Fehlersuche.
                if (string.IsNullOrWhiteSpace(ProjektOrdnerJetzt()) && !_ohneOrdnerGemeldet)
                {
                    _ohneOrdnerGemeldet = true;
                    Meldung?.Invoke("Brücke: kein Projektordner gewählt – Befehle und Schreibaufträge "
                        + "können nicht laufen. Bitte oben im Fenster den Ordner wählen.");
                }
                else if (!string.IsNullOrWhiteSpace(ProjektOrdnerJetzt()))
                {
                    _ohneOrdnerGemeldet = false;
                }

                await Dienst!.TaktAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) break;
            }
            catch (Exception ex)
            {
                // Ein Fehler im Takt darf die Brücke nicht beenden – der nächste Versuch kommt.
                _services.Logger.Write("WARN", "Brücke: Takt fehlgeschlagen – " + ex.Message);
                Dienst!.Merke(new BridgeRunInfo("takt", "intern", false, ex.Message, DateTime.Now));
            }

            ZustandGeaendert?.Invoke();

            try
            {
                await WartenAsync(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Der Projektordner, wie er <b>jetzt</b> gilt: zuerst der frische Zustand aus dem Fenster,
    /// sonst die gespeicherte Einstellung. Beides kann sich ändern, deshalb wird nicht
    /// zwischengespeichert.
    /// </summary>
    private string ProjektOrdnerJetzt()
    {
        var jetzt = OrdnerEinstellung;
        if (_gebundenerOrdner is null || string.IsNullOrWhiteSpace(jetzt)) return string.Empty;
        if (!string.Equals(Path.GetFullPath(jetzt), _gebundenerOrdner, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Projektbindung geändert – Brücke neu starten, bevor weitere Aufträge laufen.");
        return _gebundenerOrdner;
    }

    private bool _ohneOrdnerGemeldet;

    private BridgeUmgebung? _zusammenfassung;

    /// <summary>Baut das Abbild aus dem, was das Fenster liefert.</summary>
    private WorkspaceManifest AbbildErzeugen()
    {
        var umgebung = _zusammenfassung;
        var root = ProjektOrdnerJetzt();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new InvalidOperationException("Kein gültiger gebundener Projektordner – kein Abbild wird gesendet.");
        }

        lock (_manifestGate) return WorkspaceManifestBuilder.Build(
            root,
            Environment.MachineName,
            AppInfo.Version,
            _werkzeuge,
            umgebung?.Git,
            umgebung?.Protokoll,
            umgebung?.Inbox,
            umgebung?.GitStat,
            cache: _hashCache);
    }

    /// <summary>
    /// Ein Takt braucht den Rechnerzustand: der wird hier vor jedem Abbild frisch geholt und
    /// zwischengespeichert, weil <see cref="BridgeService"/> das Abbild synchron erzeugt.
    /// </summary>
    private async Task AktualisiereUmgebungAsync(CancellationToken cancellationToken)
    {
        if (Umgebung is null)
        {
            _zusammenfassung = null;
            return;
        }

        try
        {
            _zusammenfassung = await Umgebung(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _services.Logger.Write("WARN", "Brücke: Rechnerzustand nicht lesbar – " + ex.Message);
        }
    }

    private string? Lesen(string pfad)
    {
        var root = ProjektOrdnerJetzt();
        if (!BridgeProtocol.SichererPfad(root, Path.GetRelativePath(root, pfad), out var fehler))
            throw new InvalidOperationException(fehler);
        try
        {
            if (!File.Exists(pfad)) return null;
            using var input = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Datei zu groß zum Lesen (2 MiB).");
            using var reader = new StreamReader(input, Encoding.UTF8, true);
            var buffer = new char[2 * 1024 * 1024 + 1];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            if (count == buffer.Length) throw new InvalidOperationException("Datei beim Lesen gewachsen – abgebrochen.");
            return new string(buffer, 0, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private string Schreiben(string pfad, string inhalt)
        => SchreibenBytes(pfad, new UTF8Encoding(false).GetBytes(inhalt));

    private string SchreibenBytes(string pfad, byte[] inhalt)
    {
        var root = ProjektOrdnerJetzt();
        string? backup = null;
        if (File.Exists(pfad))
        {
            var relativ = Path.GetRelativePath(root, pfad);
            backup = Path.Combine(root, SyncPolicy.BackupDirName,
                "bruecke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8], relativ);
        }
        AtomicProjectFile.WriteInside(root, Path.GetRelativePath(root, pfad), inhalt, backup);
        BackupRotator.Prune(root, Math.Clamp(_bereich?.BackupCount ?? _services.Settings.BackupRunsToKeep, 1, 200));
        return $"geschrieben: {Path.GetFileName(pfad)} ({inhalt.Length:N0} Bytes)";
    }

    /// <summary>
    /// Die Freigabe eines Vorschlags. Seit rc24 gibt es <b>keine Rückfrage</b> mehr – Wunsch des
    /// Nutzers am 26.09.2026: „ich will keine popupfenster, gib alle rechte frei aus ende."
    /// </summary>
    /// <remarks>
    /// Authentifizierung, Projektpfadprüfung und Journal gelten weiterhin. Jede Aktion wird protokolliert;
    /// freie Aufrufe sind keine Betriebssystem-Sandbox. Wer die Brücke hat, kann als Benutzer Programme ausführen –
    /// das ist die bewusste Entscheidung des Nutzers, und sie steht so in docs/BRUECKE.md.
    /// </remarks>
    private bool Freigeben(BridgeJob job)
    {
        var aufruf = job.Program + (job.Argumente is { Count: > 0 } a ? " " + string.Join(' ', a) : string.Empty);
        Meldung?.Invoke("Brücke: Aufruf läuft ohne Rückfrage – " + aufruf);
        return _bereich?.AccessProfile is null or "full";
    }

    /// <summary>Sucht einmal beim Start, welche Werkzeuge vorhanden sind – das ändert sich nicht.</summary>
    private void ErmittleWerkzeuge()
    {
        try
        {
            var windows = OperatingSystem.IsWindows();
            var ordner = ToolDiscovery.StandardOrdner(windows)
                .Concat(ToolDiscovery.SplitPath(Environment.GetEnvironmentVariable("PATH"), windows))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var tool in ToolDiscovery.FindeAlle(ordner, ToolDiscovery.Endungen(windows))
                         .Where(t => t.Found && _werkzeuge.Count < 12))
            {
                _werkzeuge[tool.Name] = tool.Path;
            }
        }
        catch (Exception ex)
        {
            _services.Logger.Write("WARN", "Brücke: Werkzeugsuche fehlgeschlagen – " + ex.Message);
        }
    }

    /// <summary>Welche PowerShell läuft für die Skriptbefehle? (Antwort für das Protokoll.)</summary>
    private string SkriptEngine()
    {
        if (_werkzeuge.TryGetValue("pwsh", out var pwsh) && pwsh.Length > 0)
        {
            return "pwsh";
        }

        return _werkzeuge.TryGetValue("powershell", out var ps) && ps.Length > 0
            ? "powershell (Windows PowerShell)"
            : "keine gefunden";
    }

    private static string Anzeige(string? bin)
        => string.IsNullOrWhiteSpace(bin) ? "–" : bin.Length <= 10 ? bin : bin[..10] + "…";

    private static string Kurz(string text) => text.Length <= 60 ? text : text[..60] + "…";

    private Task? _disposeTask;
    private readonly object _disposeGate=new();
    public void Dispose()
    {
        lock(_disposeGate)
        {
            if(_entsorgt)return;_entsorgt=true;Stop();
            var loop=_lauf;var service=Dienst;
            _disposeTask=Task.Run(async()=>
            {
                try {if(loop is not null)await loop.ConfigureAwait(false);if(service is not null)await service.WaitForPublicationAsync().ConfigureAwait(false);}
                catch(Exception ex){_services.Logger.Write("WARN","RC30 Brücke beendet: "+ex.Message);}
                finally{_relay.Dispose();_wecken.Dispose();}
            });
        }
    }
}
