using System.Globalization;
using System.Collections.Concurrent;
using ArenaDesktopCompanion.Core.Agents;

namespace ArenaDesktopCompanion.Core.Bridge;

/// <summary>Wie ein Auftrag ausging – für die Anzeige im Fenster.</summary>
public sealed record BridgeRunInfo(string Id, string Art, bool Ok, string Meldung, DateTime Zeit);

/// <summary>
/// Merkt sich den zuletzt verschickten Stand des Projektordners.
/// </summary>
/// <remarks>
/// Ohne diesen Speicher hätte der Companion nach jedem Start keine Vergleichsbasis: entweder er
/// schickt wieder ein volles Abbild (286 KB bei 2 500 Dateien) oder er verschweigt die Änderungen,
/// die während seiner Pause passiert sind. Mit dem gespeicherten Stand weiß er beides genau –
/// und schickt nur, was sich wirklich geändert hat.
/// </remarks>
public interface IBridgeStandSpeicher
{
    string? Lesen();

    void Schreiben(string json);

    /// <summary>
    /// Der gespeicherte <b>Änderungsverlauf</b> (Ring aus <see cref="BridgeChronik"/>). Er gehört
    /// dazu, sonst ist er nach jedem Start leer (Befund vom 21.09.2026, Abnahmepunkt 8.9).
    /// </summary>
    string? LesenChronik();

    void SchreibenChronik(string json);
}

/// <summary>Standspeicher im Arbeitsspeicher – Standard, etwa für Prüfungen.</summary>
public sealed class SpeicherStand : IBridgeStandSpeicher
{
    private string? _json;
    private string? _chronik;

    public string? Lesen() => _json;

    public void Schreiben(string json) => _json = json;

    public string? LesenChronik() => _chronik;

    public void SchreibenChronik(string json) => _chronik = json;
}

/// <summary>
/// Standspeicher in einer Datei neben <c>settings.json</c>. Ein Schreibfehler ist kein Grund
/// aufzuhören: dann gibt es beim nächsten Start eben einmal wieder ein volles Abbild.
/// </summary>
public sealed class DateiStand : IBridgeStandSpeicher
{
    private readonly string _pfad;
    private readonly string _chronikPfad;

    public DateiStand(string pfad)
    {
        _pfad = pfad;

        // Der Verlauf liegt als kleine Datei daneben: bridge-stand.json und bridge-chronik.json.
        var ordner = Path.GetDirectoryName(pfad);
        var name = Path.GetFileName(pfad);
        name = string.Equals(name, "bridge-stand.json", StringComparison.OrdinalIgnoreCase)
            ? "bridge-chronik.json"
            : name + ".chronik";
        _chronikPfad = string.IsNullOrEmpty(ordner) ? name : Path.Combine(ordner, name);
    }

    public string? Lesen() => Lies(_pfad);

    public void Schreiben(string json) => Schreib(_pfad, json);

    public string? LesenChronik() => Lies(_chronikPfad);

    public void SchreibenChronik(string json) => Schreib(_chronikPfad, json);

    private static string? Lies(string pfad)
    {
        try
        {
            return File.Exists(pfad) ? File.ReadAllText(pfad) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Schreib(string pfad, string json)
    {
        try
        {
            var ordner = Path.GetDirectoryName(pfad);
            if (!string.IsNullOrEmpty(ordner))
            {
                Directory.CreateDirectory(ordner);
            }

            File.WriteAllText(pfad, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Beim nächsten Start gibt es einmal wieder ein volles Abbild – verkraftbar.
        }
    }
}

/// <summary>
/// Merkt sich, welche Aufträge schon erledigt sind. Wichtig ist das über einen Neustart hinweg:
/// Ohne Gedächtnis würde der Companion nach jedem Start alles wiederholen, was noch im Briefkasten
/// liegt – ein <c>build</c> liefe erneut, eine geschriebene Datei würde erneut geschrieben.
/// </summary>
public interface IBridgeGedaechtnis
{
    bool Kenne(string kennung);

    void Merke(string kennung);
}

/// <summary>Gedächtnis im Arbeitsspeicher – Standard, etwa für Prüfungen.</summary>
public sealed class BridgeGedaechtnis : IBridgeGedaechtnis
{
    private readonly HashSet<string> _kennungen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _schloss = new();

    public bool Kenne(string kennung)
    {
        lock (_schloss)
        {
            return _kennungen.Contains(kennung);
        }
    }

    public void Merke(string kennung)
    {
        lock (_schloss)
        {
            _kennungen.Add(kennung);
        }
    }
}

/// <summary>
/// Gedächtnis in einer Datei: eine Kennung je Zeile, die neuesten am Ende. Die Datei wächst nicht
/// unbegrenzt – alte Kennungen fallen oben heraus. Kann sie nicht geschrieben werden, ist das kein
/// Grund aufzuhören: dann wiederholt der Companion im Zweifel einen Auftrag, statt still zu stehen.
/// </summary>
public sealed class DateiGedaechtnis : IBridgeGedaechtnis
{
    private const int MaxZeilen = 1000;

    private readonly string _pfad;
    private readonly HashSet<string> _kennungen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _reihenfolge = [];
    private readonly object _schloss = new();

    public DateiGedaechtnis(string pfad)
    {
        _pfad = pfad;
        Lesen();
    }

    public IReadOnlyCollection<string> Bekannt => _kennungen;

    private void Lesen()
    {
        try
        {
            if (!File.Exists(_pfad))
            {
                return;
            }

            foreach (var zeile in File.ReadLines(_pfad))
            {
                var kennung = zeile.Trim();
                if (kennung.Length > 0 && _kennungen.Add(kennung))
                {
                    _reihenfolge.Add(kennung);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Gedächtnis weiterarbeiten – im Zweifel wird ein Auftrag wiederholt.
        }
    }

    public bool Kenne(string kennung)
    {
        lock (_schloss)
        {
            return _kennungen.Contains(kennung);
        }
    }

    public void Merke(string kennung)
    {
        lock (_schloss)
        {
            if (_kennungen.Contains(kennung)) return;
            var neu = _reihenfolge.Append(kennung).TakeLast(MaxZeilen).ToList();
            AtomicProjectFile.ReplaceBytes(_pfad, System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, neu) + Environment.NewLine));
            _reihenfolge.Clear();
            _reihenfolge.AddRange(neu);
            _kennungen.Clear();
            foreach (var id in neu) _kennungen.Add(id);
        }
    }

}

/// <summary>
/// Der Kreislauf der Brücke: Abbild schicken, Aufträge abholen, ausführen, Ergebnis zurücklegen.
/// </summary>
/// <remarks>
/// <para>
/// Ein Durchgang (<see cref="TaktAsync"/>) macht genau vier Dinge, in dieser Reihenfolge:
/// <list type="number">
/// <item>Ist ein Lebenszeichen fällig? Dann <c>hello.json</c> schreiben.</item>
/// <item>Hat sich der Ordner geändert? Dann ein neues Abbild schreiben (gedrosselt).</item>
/// <item>Liegt ein Auftrag bereit? Dann prüfen (siehe <see cref="BridgeProtocol.Pruefe"/>) und ausführen.</item>
/// <item>Ergebnis zurücklegen und örtlich merken, damit derselbe Auftrag nicht zweimal läuft.</item>
/// </list>
/// Alles andere, was im Briefkasten liegt, wird ignoriert.
/// </para>
/// <para>
/// Der Dienst läuft im Hintergrund der App; jeder Schritt ist gegen Fehler des Netzes abgesichert,
/// damit ein nicht erreichbarer Briefkasten das Fenster nicht stört.
/// </para>
/// </remarks>
public sealed class BridgeService
{
    private readonly RelayClient _relay;
    private readonly CommandRunner _runner;
    private readonly Func<string, string?> _dateiLesen;
    private readonly Func<string, string, string> _dateiSchreiben;

    /// <summary>Schreibt Binärinhalte (Base64-Aufträge) – <c>null</c>, wenn die Gegenseite das nicht anbietet.</summary>
    private readonly Func<string, byte[], string>? _dateiSchreibenBytes;
    private readonly Func<string> _token;
    private readonly Func<WorkspaceManifest> _abbildErzeugen;
    private readonly Action<string>? _log;
    private readonly Func<BridgeJob, bool> _freigeben;

    public BridgeJobJournal? Journal { get; set; }
    private readonly IBridgeGedaechtnis _gedaechtnis;
    private readonly IBridgeStandSpeicher _standSpeicher;
    private readonly BridgeChronik _chronik = new();
    private bool _grundlageGeholt;
    private DateTime _letztesSchwer = DateTime.MinValue;
    private readonly List<BridgeRunInfo> _verlauf = [];
    private string? _letzterAbbildHash;
    private DateTime _letztesAbbild = DateTime.MinValue;
    private DateTime _letztesHello = DateTime.MinValue;

    /// <summary>Der laufende Abbild-Lauf (im Hintergrund) und der letzte fertige Stand.</summary>
    private readonly object _abbildTor = new();
    private Task<WorkspaceManifest>? _abbildLauf;
    private WorkspaceManifest? _letztesFertigesAbbild;
    private bool _abbildGekuerzt;
    private DateTime _letzteAbbildWarnung = DateTime.MinValue;

    public BridgeService(
        RelayClient relay,
        CommandRunner runner,
        Func<string, string?> dateiLesen,
        Func<string, string, string> dateiSchreiben,
        Func<string> token,
        Func<WorkspaceManifest> abbildErzeugen,
        Action<string>? log = null,
        IBridgeGedaechtnis? gedaechtnis = null,
        IBridgeStandSpeicher? standSpeicher = null,
        Func<BridgeJob, bool>? freigeben = null,
        Func<string, byte[], string>? dateiSchreibenBytes = null)
    {
        _relay = relay;
        _runner = runner;
        _dateiLesen = dateiLesen;
        _dateiSchreiben = dateiSchreiben;
        _token = token;
        _abbildErzeugen = abbildErzeugen;
        _log = log;
        _gedaechtnis = gedaechtnis ?? new BridgeGedaechtnis();
        _standSpeicher = standSpeicher ?? new SpeicherStand();
        _freigeben = freigeben ?? (_ => false);   // ohne Freigabefenster wird nichts vorgeschlagen
        _dateiSchreibenBytes = dateiSchreibenBytes;
    }

    /// <summary>Normale Lebenszyklussteuerung; Authentifizierung und Pfadschutz bleiben erhalten.</summary>
    public bool RequirePeerConfirmation { get; init; }
    public bool BefehleErlaubt { get; set; } = true;

    /// <summary>Normale Lebenszyklussteuerung; Authentifizierung und Pfadschutz bleiben erhalten.</summary>
    public bool SchreibendErlaubt { get; set; } = true;

    /// <summary>Normale Lebenszyklussteuerung; Authentifizierung und Pfadschutz bleiben erhalten.</summary>
    public bool VorschlaegeErlaubt { get; set; } = true;

    /// <summary>Wie oft höchstens ein neues Abbild geschickt wird.</summary>
    public TimeSpan AbbildAbstand { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Wie oft ein Lebenszeichen geschickt wird.</summary>
    public TimeSpan HelloAbstand { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Wie oft höchstens das <b>schwere</b> Abbild geschickt wird (bei 2 500 Dateien 286 KB).
    /// Kleine Änderungen gehen sofort als Verlauf und Kurzfassung raus – ein paar Kilobyte –,
    /// der volle Stand nur in diesem Abstand oder wenn er ausdrücklich angefordert wird. Sonst
    /// ginge bei jedem Tastendruck im Editor das ganze Abbild über die Leitung.
    /// </summary>
    public TimeSpan SchwerAbstand { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// So lange darf ein Takt auf ein frisches Ordner-Abbild warten. Ist es nicht fertig, gilt der
    /// letzte fertige Stand und der Lauf geht im Hintergrund weiter.
    /// </summary>
    /// <remarks>
    /// Anlass (24.09.2026, live): Ein großer Baum im Projektordner machte die Hash-Berechnung so
    /// langsam, dass die Brücke in dieser Zeit vollständig schwieg – von außen sah es aus, als wäre
    /// der Companion beendet (kein Lebenszeichen, kein Auftrag abgeholt). Ein unvollständiges Abbild
    /// ist besser als eine schweigende Brücke.
    /// </remarks>
    public TimeSpan AbbildZeitgrenze { get; set; } = TimeSpan.FromSeconds(4);

    public IReadOnlyList<BridgeRunInfo> Verlauf { get { lock (_historyGate) return _verlauf.ToArray(); } }

    public DateTime? LetzterKontakt { get; private set; }

    public string? LetzterFehler { get; private set; }

    /// <summary>Wie oft ein Takt etwas getan hat – für die Anzeige.</summary>
    public int Takte { get; private set; }

    /// <summary>
    /// Ein Durchgang. Gibt zurück, was passiert ist – für das Protokoll im Fenster.
    /// </summary>
    public bool BackgroundCommands { get; set; }
    public bool HasRunningCommands => _commandJobs.Values.Any(t=>!t.IsCompleted);
    private readonly ConcurrentDictionary<string,Task> _commandJobs=new();
    private readonly SemaphoreSlim _commandGate=new(1,1);
    private Task? _publikation;
    private readonly SemaphoreSlim _publicationGate=new(1,1);
    private readonly ConcurrentDictionary<string,Task> _manifestJobs=new();
    private readonly ConcurrentQueue<BridgeRunInfo> _completed=new();
    private readonly object _historyGate=new();
    public Task WaitForPublicationAsync() => Task.WhenAll(_manifestJobs.Values.Concat(_commandJobs.Values).Append(_publikation??Task.CompletedTask));
    private void Record(BridgeRunInfo info)
    {
        lock(_historyGate){_verlauf.Add(info);if(_verlauf.Count>400)_verlauf.RemoveAt(0);}
        _log?.Invoke($"{info.Art}: {info.Meldung}");
    }
    private async Task RunManifestJobAsync(BridgeJob job,CancellationToken token)
    {
        try {var info=await ErledigeAsync(job,token).ConfigureAwait(false);Record(info);_completed.Enqueue(info);lock(_peerGate){if(PeerConfirmed&&info.Ok){PeerLastSeen=DateTimeOffset.UtcNow;PeerRestored=false;}}AcceptedJob?.Invoke(job);}
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex) when(ex is not OutOfMemoryException){LetzterFehler=ex.Message;_log?.Invoke("Abbildauftrag: "+ex.Message);}
    }
    private async Task RunQueuedCommandAsync(BridgeJob job,string root,string connection,CancellationToken token)
    {
        var held=false;
        try
        {
            await _commandGate.WaitAsync(token).ConfigureAwait(false);held=true;await Task.Yield();
            if(ConnectionId!=connection||ProjektOrdnerJetzt()!=root) { await MeldeAsync(job,"abgelehnt",null,-1,0,"Binding changed while queued; no action executed.",token).ConfigureAwait(false);return; }
            var info=await ErledigeAsync(job,token).ConfigureAwait(false);Record(info);_completed.Enqueue(info);
            lock(_peerGate){if(PeerConfirmed&&info.Ok){PeerLastSeen=DateTimeOffset.UtcNow;PeerRestored=false;}}
            AcceptedJob?.Invoke(job);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception ex) when(ex is not OutOfMemoryException){LetzterFehler=ex.Message;_log?.Invoke("Queued command: "+ex.Message);}
        finally{if(held)_commandGate.Release();}
    }
    public string? PublikationsFehler { get; private set; }
    private readonly object _peerGate=new();
    private string _connectionId = "";
    public string ConnectionId { get {lock(_peerGate)return _connectionId;} set {lock(_peerGate){if (_connectionId != value) { _connectionId = value; PeerConfirmed = false; PeerLastSeen = null; PeerRestored=false; }}} }
    public (string Id,DateTimeOffset Seen)? ConfirmedPeerSnapshot(){lock(_peerGate)return PeerConfirmed&&PeerLastSeen is{} seen?(_connectionId,seen):null;}
    public bool PeerRestored { get; private set; }
    public bool RestoreConfirmedPeer(string connectionId,DateTimeOffset lastSeen)
    {
        lock(_peerGate){if(_connectionId.Length==0||_connectionId!=connectionId||lastSeen>DateTimeOffset.UtcNow.AddMinutes(5))return false;PeerConfirmed=true;PeerLastSeen=lastSeen;PeerRestored=true;return true;}
    }
    public DateTimeOffset? PeerLastSeen { get; private set; }
    public bool PeerConfirmed { get; private set; }
    public Action<BridgeJob>? AcceptedJob { get; set; }

    public async Task<IReadOnlyList<BridgeRunInfo>> TaktAsync(CancellationToken cancellationToken = default)
    {
        var neu = new List<BridgeRunInfo>();
        while(_completed.TryDequeue(out var completed))neu.Add(completed);
        foreach(var pair in _manifestJobs.Where(p=>p.Value.IsCompleted).ToArray())_manifestJobs.TryRemove(pair.Key,out _);
        if (!RelayClient.NameIstGueltig(_relay.Bin)) return neu;
        foreach(var pair in _commandJobs.Where(p=>p.Value.IsCompleted).ToArray())_commandJobs.TryRemove(pair.Key,out _);
        Takte++;
        try
        {
            // Control is first. A manifest failure must never silence Hello or the job queue.
            if (DateTime.Now - _letztesHello >= HelloAbstand)
            {
                try
                {
                    await _relay.PutAsync(BridgeProtocol.HelloDatei, BridgeProtocol.ToJson(new BridgeHello
                    {
                        ConnectionId=ConnectionId,
                        AppVersion = AppInfo.Version, Host = Environment.MachineName, Projekt = ProjektOrdnerJetzt(),
                        Tools = _letzteWerkzeuge, Befehle = CommandCatalog.Verfuegbar(OperatingSystem.IsWindows(), BefehleErlaubt, SchreibendErlaubt).Select(c => c.Id).ToList(),
                        Zeit = Jetzt(), Sequenz = _chronik.Sequenz, Stand = _letzterStand,
                    }), cancellationToken).ConfigureAwait(false);
                    _letztesHello = DateTime.Now;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException) { _log?.Invoke("Lebenszeichen: " + ex.Message); }
            }
            var dateien = await _relay.ListAsync(cancellationToken).ConfigureAwait(false);
            Journal?.Reconcile(dateien.Where(d => d.Name.StartsWith(BridgeProtocol.JobPraefix, StringComparison.Ordinal) && d.Name.EndsWith(".json", StringComparison.Ordinal))
                .Select(d => d.Name[BridgeProtocol.JobPraefix.Length..^5]).ToHashSet(StringComparer.Ordinal));
            LetzterKontakt = DateTime.Now;
            LetzterFehler = null;
            foreach (var datei in dateien.Where(d => d.Name.StartsWith(BridgeProtocol.JobPraefix, StringComparison.Ordinal) && d.Name.EndsWith(".json", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = datei.Name[BridgeProtocol.JobPraefix.Length..^5];
                if (id.Length is 0 or > 96 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) continue;
                // Each message has its own failure boundary. Poison messages cannot block the queue.
                try
                {
                    if(_manifestJobs.ContainsKey(id)||_commandJobs.ContainsKey(id))continue;
                    if (_gedaechtnis.Kenne(id) || Journal?.State(id) is not null)
                    {
                        if (!dateien.Any(d => d.Name == BridgeJob.ErgebnisName(id)))
                        {
                            var saved = Journal?.Result(id);
                            if (saved is not null) await _relay.PutAsync(BridgeJob.ErgebnisName(id), saved, cancellationToken).ConfigureAwait(false);
                            else await MeldeAsync(new BridgeJob { Id = id }, "fehler", null, -1, 0, "Unterbrochener oder bereits zugestellter Auftrag wird nicht wiederholt.", cancellationToken).ConfigureAwait(false);
                        }
                        continue;
                    }
                    var job = BridgeProtocol.FromJson<BridgeJob>(await _relay.GetAsync(datei.Name, cancellationToken).ConfigureAwait(false));
                    if (job is null || job.Id != id) throw new InvalidDataException("Auftrags-ID/JSON ungültig.");
                    if (_relay.TransportSecured)
                    {
                        if (!string.Equals(job.Projekt?.TrimEnd('\\', '/'), ProjektOrdnerJetzt().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Falsche Projektbindung.");
                        job = job with { Token = _token() };
                    }
                    var valid = BridgeProtocol.Pruefe(job, _token(), ProjektOrdnerJetzt(), BefehleErlaubt, SchreibendErlaubt, VorschlaegeErlaubt);
                    if (!valid.Ok) throw new InvalidDataException(valid.Fehler);
                    if(RequirePeerConfirmation&&!PeerConfirmed&&job.KindAlsEnum!=JobKind.Ping)
                    {
                        _gedaechtnis.Merke(id);
                        await MeldeAsync(job,"fehler",null,-1,0,"AXON_HANDSHAKE_REQUIRED",cancellationToken).ConfigureAwait(false);
                        var denied=new BridgeRunInfo(id,job.Kind,false,"Projektaktion ohne bestätigte Chatbindung verweigert",DateTime.Now);neu.Add(denied);Record(denied);continue;
                    }
                    Journal?.Begin(id);
                    _gedaechtnis.Merke(id);
                    if(job.KindAlsEnum is JobKind.Manifest or JobKind.Look)
                    {
                        if(_manifestJobs.Count>=8){await MeldeAsync(job,"fehler",null,-1,0,"Zu viele parallele Abbildaufträge (maximal 8).",cancellationToken).ConfigureAwait(false);continue;}
                        _manifestJobs[id]=Task.CompletedTask;
                        _manifestJobs[id]=Task.Run(()=>RunManifestJobAsync(job,cancellationToken),CancellationToken.None);
                        continue;
                    }
                    if(BackgroundCommands && job.KindAlsEnum is JobKind.Run or JobKind.Vorschlag or JobKind.Write)
                    {
                        if(_commandJobs.Count>=8){await MeldeAsync(job,"fehler",null,-1,0,"Command queue full (max 8); no action executed.",cancellationToken).ConfigureAwait(false);continue;}
                        var project=ProjektOrdnerJetzt();var connection=ConnectionId;
                        _commandJobs[id]=Task.CompletedTask;
                        _commandJobs[id]=RunQueuedCommandAsync(job,project,connection,cancellationToken);
                        continue;
                    }
                    var info = await ErledigeAsync(job, cancellationToken).ConfigureAwait(false);
                    lock(_peerGate)
                    {
                        if(job.KindAlsEnum==JobKind.Ping&&job.Note=="AXON-ACK:"+_connectionId&&_connectionId.Length>0&&info.Ok)PeerConfirmed=true;
                        if(PeerConfirmed){PeerLastSeen=DateTimeOffset.UtcNow;PeerRestored=false;}
                    }
                    AcceptedJob?.Invoke(job);
                    neu.Add(info);Record(info);
                }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidDataException or System.Text.Json.JsonException)
                {
                    _log?.Invoke($"Auftrag {id} abgewiesen: {ex.Message}");
                    _gedaechtnis.Merke(id);
                    await _relay.DeleteAsync(datei.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
                { LetzterFehler = ex.Message; _log?.Invoke($"Auftrag {id}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        { LetzterFehler = ex.Message; }
        if (_publikation is null || _publikation.IsCompleted)
            _publikation = PublishAsync(cancellationToken);
        return neu;
    }

    private async Task PublishAsync(CancellationToken token)
    {
        try
        {
            await _publicationGate.WaitAsync(token).ConfigureAwait(false);
            try {await SendeAbbildAsync(token).ConfigureAwait(false);PublikationsFehler=null;}
            finally {_publicationGate.Release();}
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { PublikationsFehler = ex.Message; }
    }

    /// <summary>
    /// Holt das Ordner-Abbild, ohne den Takt aufzuhalten: der Lauf startet im Hintergrund, gewartet
    /// wird höchstens <see cref="AbbildZeitgrenze"/>. Ist er dann nicht fertig, kommt der letzte
    /// fertige (oder der gespeicherte) Stand zurück – und der Lauf läuft weiter.
    /// </summary>
    private async Task<WorkspaceManifest?> AbbildBesorgenAsync(CancellationToken cancellationToken)
    {
        Task<WorkspaceManifest> lauf;
        lock (_abbildTor)
        {
            _abbildLauf ??= Task.Run(_abbildErzeugen, CancellationToken.None);
            lauf = _abbildLauf;
        }

        var fertig = await MitZeitgrenzeAsync(lauf, AbbildZeitgrenze, cancellationToken).ConfigureAwait(false);
        if (fertig is not null)
        {
            lock (_abbildTor)
            {
                _letztesFertigesAbbild = fertig;
                if (ReferenceEquals(_abbildLauf, lauf))
                {
                    _abbildLauf = null;
                }
            }
            return fertig;
        }

        var note = lauf.IsFaulted
            ? "Ordner-Abbild fehlgeschlagen: " + (lauf.Exception?.GetBaseException().Message ?? "unbekannt")
            : $"Ordner-Abbild dauert länger als {AbbildZeitgrenze.TotalSeconds:0} s";

        bool schonGemeldet;
        lock (_abbildTor)
        {
            schonGemeldet = DateTime.Now - _letzteAbbildWarnung < TimeSpan.FromMinutes(5);
            if (!schonGemeldet)
            {
                _letzteAbbildWarnung = DateTime.Now;
            }
            if (lauf.IsFaulted || lauf.IsCanceled)
            {
                _abbildLauf = null;
            }
        }

        if (!schonGemeldet)
        {
            _log?.Invoke("Brücke: " + note + " – es gilt der letzte fertige Stand, die Brücke läuft weiter.");
        }

        lock (_abbildTor)
        {
            if (_letztesFertigesAbbild is not null)
            {
                return _letztesFertigesAbbild;
            }
        }

        // Zuletzt gespeicherter Stand: lieber ein altes Abbild als gar keins.
        return _letztesAbbildJson is null ? null : BridgeProtocol.FromJson<WorkspaceManifest>(_letztesAbbildJson);
    }

    /// <summary>
    /// Wartet höchstens <paramref name="grenze"/> auf eine Aufgabe; danach kommt <c>null</c> zurück.
    /// Die Aufgabe läuft weiter. Fehler der Aufgabe ergeben ebenfalls <c>null</c> – der Takt darf an
    /// einem langsamen oder fehlerhaften Abbild nicht scheitern.
    /// </summary>
    /// <summary>
    /// Einmal melden, wenn der Projektordner größer ist, als der Briefkasten aufnehmen kann.
    /// Einmalig, weil der Zustand für jeden Takt gilt – eine Zeile je Takt wäre genau das Karussell,
    /// das am 24.09.2026 aufgefallen ist.
    /// </summary>
    private void AbbildGroesseMelden(WorkspaceManifest abbild)
    {
        var gekuerzt = abbild.Counts.GetValueOrDefault("truncated") == 1;
        if (gekuerzt == _abbildGekuerzt)
        {
            return;
        }

        _abbildGekuerzt = gekuerzt;
        if (gekuerzt)
        {
            _log?.Invoke("Brücke: Der Projektordner ist größer, als in den Briefkasten passt – das Abbild ist "
                + $"gekürzt ({abbild.Counts.GetValueOrDefault("files")} Einträge). Für Fragen einen Unterordner nennen lassen.");
        }
    }

    public static async Task<T?> MitZeitgrenzeAsync<T>(Task<T> lauf, TimeSpan grenze, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(lauf);

        var abgelaufen = Task.Delay(grenze, cancellationToken);
        var fertig = await Task.WhenAny(lauf, abgelaufen).ConfigureAwait(false);
        if (fertig == lauf && lauf.IsCompletedSuccessfully)
        {
            return lauf.Result;
        }

        // Damit eine spätere Ausnahme nicht unbeobachtet bleibt.
        _ = lauf.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return null;
    }

    private async Task SendeAbbildAsync(CancellationToken cancellationToken)
    {
        if (DateTime.Now - _letztesAbbild < AbbildAbstand)
        {
            return;
        }

        if (!_grundlageGeholt)
        {
            _grundlageGeholt = true;
            // Der Änderungsverlauf überlebt den Neustart: sonst zeigte die Chronik nach jedem
            // Start nur die Änderungen, die danach erkannt wurden – alles aus der Pause wäre weg,
            // obwohl der Stand sie kennt (Befund vom 21.09.2026, Abnahmepunkt 8.9).
            _chronik.AusSpeicher(_standSpeicher.LesenChronik());

            var gespeichert = _standSpeicher.Lesen();
            if (!string.IsNullOrWhiteSpace(gespeichert) && _letztesAbbildJson is null)
            {
                _letztesAbbildJson = gespeichert;
                var voriger = BridgeProtocol.FromJson<WorkspaceManifest>(gespeichert);
                if (voriger is not null)
                {
                    // Gleicher Maßstab wie unten: der Fingerabdruck des Bestands, nicht des Roh-JSON.
                    _letzterAbbildHash = WorkspaceManifestBuilder.StandHash(voriger);
                    _letzterKleinHash = WorkspaceManifestBuilder.KleinHash(voriger);
                }
            }
        }

        var abbild = await AbbildBesorgenAsync(cancellationToken).ConfigureAwait(false);
        if (abbild is null)
        {
            // Noch kein fertiger Stand: dann eben keins – aber die Brücke lebt weiter (Lebenszeichen,
            // Aufträge). Der nächste Takt versucht es erneut.
            _letztesAbbild = DateTime.Now;
            return;
        }

        _letzteWerkzeuge = abbild.Tools.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var json = WorkspaceManifestBuilder.ToJson(abbild);
        AbbildGroesseMelden(abbild);
        var standHash = WorkspaceManifestBuilder.StandHash(abbild);
        var kleinHash = WorkspaceManifestBuilder.KleinHash(abbild);
        var bestandGleich = string.Equals(standHash, _letzterAbbildHash, StringComparison.Ordinal);
        var kleinGleich = string.Equals(kleinHash, _letzterKleinHash, StringComparison.Ordinal);

        if (bestandGleich && kleinGleich && !_schwerOffen)
        {
            _letztesAbbild = DateTime.Now; // nichts Neues: kein Datenverkehr
            return;
        }

        var vorher = _letztesAbbildJson is null ? null : BridgeProtocol.FromJson<WorkspaceManifest>(_letztesAbbildJson);

        if (!bestandGleich)
        {
            // Nur wenn sich am Bestand etwas geändert hat, gibt es Einträge für die Chronik.
            var leer = new WorkspaceManifest { Time = string.Empty, Root = abbild.Root, Files = [] };
            var (neu, geaendert, weg) = WorkspaceManifestBuilder.Diff(vorher ?? leer, abbild);
            if (vorher is null)
            {
                // Ganz ohne Vergleichsbasis: der erste Stand wird nicht als „alles neu“ gemeldet.
                neu = [];
                geaendert = [];
                weg = [];
            }

            if (neu.Count + geaendert.Count + weg.Count > 0)
            {
                var schnelle = abbild.Files.ToDictionary(f => f.Path, f => (f.Bytes, f.Hash), StringComparer.Ordinal);
                _chronik.Aufnehmen(neu, geaendert, weg, DateTime.Now,
                    pfad => schnelle.TryGetValue(pfad, out var masse) ? masse : (0L, string.Empty));
            }
        }

        // Das schwere Abbild geht höchstens alle <see cref="SchwerAbstand"/> raus – wenn es aber
        // aussteht, dann auch wirklich, sobald die Zeit um ist. Befund vom 21.09.2026: Bis rc10 wurde
        // ein aufgeschobenes Abbild vergessen, weil der Fingerabdruck trotzdem fortgeschrieben wurde –
        // die andere Seite sah dann minuten- bis stundenlang einen veralteten Gesamtstand, ohne dass
        // etwas auf einen Blick erkennbar wäre. Der billige Weg (Kurzfassung + Verlauf) war immer
        // aktuell; nur der volle Stand hing.
        if (!bestandGleich || _schwerOffen)
        {
            if (DateTime.Now - _letztesSchwer >= SchwerAbstand)
            {
                await _relay.PutAsync(BridgeProtocol.ManifestDatei, json, cancellationToken).ConfigureAwait(false);
                _letztesSchwer = DateTime.Now;
                _schwerOffen = false;
            }
            else
            {
                _schwerOffen = true;   // beim nächsten Takt nachholen
            }
        }

        // Die kleinen Dinge kommen immer mit: Kurzfassung und Verlauf kosten nur Kilobyte.
        await _relay.PutAsync(BridgeProtocol.ManifestLesbarDatei,
            WorkspaceManifestBuilder.ToMarkdown(abbild, vorher), cancellationToken).ConfigureAwait(false);

        _letzterStand = Kurzstand(abbild);
        await _relay.PutAsync(BridgeProtocol.ChronikDatei, _chronik.ToJson(_letzterStand), cancellationToken)
            .ConfigureAwait(false);

        _letzterAbbildHash = standHash;
        _letzterKleinHash = kleinHash;
        _letztesAbbildJson = json;
        _letztesAbbild = DateTime.Now;
        _standSpeicher.Schreiben(json);
        _standSpeicher.SchreibenChronik(_chronik.ToSpeicher());
    }

    private string? _letztesAbbildJson;

    /// <summary>Steht ein schweres Abbild aus, weil der Abstand noch nicht um war?</summary>
    private bool _schwerOffen;
    private string? _letzterKleinHash;
    private IReadOnlyList<string> _letzteWerkzeuge = [];
    private BridgeStand? _letzterStand;

    private async Task<BridgeRunInfo> ErledigeAsync(BridgeJob job, CancellationToken cancellationToken)
    {
        var project = ProjektOrdnerJetzt();
        var art = job.KindAlsEnum ?? JobKind.Look;
        // Ein Vorschlag ist immer ein schreibender Eingriff – hier zählt allein das Ja des Nutzers
        // im Freigabefenster, nicht die Schreibfreigabe (die für die feste Liste gilt).
        var (ok, fehler) = BridgeProtocol.Pruefe(job, _token(), project, BefehleErlaubt,
            SchreibendErlaubt || art == JobKind.Vorschlag, VorschlaegeErlaubt);
        if (!ok)
        {
            await MeldeAsync(job, "abgelehnt", null, -1, 0, fehler, cancellationToken).ConfigureAwait(false);
            return new BridgeRunInfo(job.Id, art.ToString(), false, "abgelehnt: " + fehler, DateTime.Now);
        }

        try
        {
            switch (art)
            {
                case JobKind.Ping:
                    if(RequirePeerConfirmation&&(job.Note?.StartsWith("AXON-CONNECT:",StringComparison.Ordinal)==true||job.Note?.StartsWith("AXON-ACK:",StringComparison.Ordinal)==true)&&job.Note!="AXON-CONNECT:"+ConnectionId&&job.Note!="AXON-ACK:"+ConnectionId)
                    {
                        await MeldeAsync(job,"fehler",null,-1,0,"Connect-Bindung geändert: aktuellen Connect-Text neu senden.",cancellationToken).ConfigureAwait(false);
                        return new BridgeRunInfo(job.Id,"ping",false,"Veraltete Connect-ID abgewiesen",DateTime.Now);
                    }
                    await MeldeAsync(job, "ok", project, 0, 0, null, cancellationToken, redigieren: false).ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, "ping", true, "Projekt und Hin-/Rückweg bestätigt", DateTime.Now);
                case JobKind.Run:
                {
                    var befehl = CommandCatalog.Find(job.Command!)!;
                    var ergebnis = await _runner.RunAsync(project, befehl, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    await MeldeAsync(job, ergebnis.Ok ? "ok" : "fehler", ergebnis.Output, ergebnis.ExitCode,
                        ergebnis.Duration.TotalSeconds, ergebnis.StartError ?? (ergebnis.TimedOut ? "Zeit abgelaufen" : null),
                        cancellationToken).ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, "run:" + befehl.Id, ergebnis.Ok, ergebnis.Zusammenfassung(), DateTime.Now);
                }

                case JobKind.Vorschlag:
                {
                    // The host enforces the explicitly selected session access profile. No per-job popup.
                    var erlaubt = false;
                    try
                    {
                        erlaubt = _freigeben(job);
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke("Brücke: Freigabefrage fehlgeschlagen – " + ex.Message);
                    }

                    var aufruf = job.Program + " " + string.Join(' ', job.Argumente ?? []);
                    if (!erlaubt)
                    {
                        await MeldeAsync(job, "abgelehnt", aufruf, -1, 0, "vom Nutzer nicht freigegeben", cancellationToken)
                            .ConfigureAwait(false);
                        return new BridgeRunInfo(job.Id, "vorschlag", false, "abgelehnt: nicht freigegeben", DateTime.Now);
                    }

                    var spec = CommandCatalog.Vorschlag(job.Program!, job.Argumente ?? [],
                        job.Zweck ?? string.Empty);
                    var lauf = await _runner.RunAsync(project, spec, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    await MeldeAsync(job, lauf.Ok ? "ok" : "fehler", lauf.Output, lauf.ExitCode,
                        lauf.Duration.TotalSeconds,
                        lauf.StartError ?? (lauf.TimedOut ? "Zeit abgelaufen" : null), cancellationToken)
                        .ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, "vorschlag", lauf.Ok,
                        $"freigegeben und ausgeführt: {aufruf} ({lauf.Duration.TotalSeconds:F1} s)", DateTime.Now);
                }

                case JobKind.Read:
                {
                    var text = _dateiLesen(VollerPfad(project, job.Path!));
                    if (text is null)
                    {
                        // „nicht lesbar oder nicht vorhanden" war für zwei verschiedene Fälle derselbe
                        // Satz – wer einen Ordner liest, hat lange gerätselt (rc18).
                        var istOrdner = Directory.Exists(VollerPfad(project, job.Path!));
                        var grund = istOrdner
                            ? $"»{job.Path}« ist ein Ordner, keine Datei. Einzelne Dateien daraus nennen – z. B. über den Stand."
                            : "Datei nicht lesbar oder nicht vorhanden.";
                        await MeldeAsync(job, "fehler", grund, 1, 0,
                            istOrdner ? "ist ein Ordner" : "nicht gefunden", cancellationToken).ConfigureAwait(false);
                        return new BridgeRunInfo(job.Id, "read", false, istOrdner ? "ist ein Ordner" : "nicht gefunden", DateTime.Now);
                    }

                    // Befund vom 21.09.2026 (TODO 8.18): Hier wurde der Inhalt bis rc12 redigiert.
                    // Wer ihn danach zurückschrieb, verlor Code (Platzhalter statt Zeilen, vier
                    // Compilerfehler). Der Inhalt geht deshalb unverändert hinaus – die Redaktion
                    // greift dort, wo sie hingehört: bei Befehlsausgaben und Protokollen. Wie
                    // verdächtig der Inhalt ist, steht als Hinweis dabei, damit es niemand
                    // versehentlich weiterreicht.
                    var (_, verdaechtig) = SecretRedaction.CleanMitAnzahl(text);
                    var hinweis = verdaechtig == 0
                        ? null
                        : $"{verdaechtig} Stelle(n) im Inhalt sehen wie ein Schlüssel aus. Sie wurden NICHT entfernt, "
                          + "damit die Datei unverändert bleibt – bitte nicht in Protokolle oder Chats kopieren.";
                    await MeldeAsync(job, "ok", text, 0, 0, hinweis, cancellationToken, redigieren: false)
                        .ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, "read", true,
                        $"{job.Path} gelesen ({text.Length} Zeichen)"
                        + (verdaechtig > 0 ? $", {verdaechtig} verdächtige Stelle(n)" : string.Empty),
                        DateTime.Now);
                }

                case JobKind.Write:
                {
                    if (job.IstBase64 && _dateiSchreibenBytes is null)
                    {
                        const string grund = "Binärinhalte kann diese Seite nicht ablegen.";
                        await MeldeAsync(job, "fehler", null, -1, 0, grund, cancellationToken).ConfigureAwait(false);
                        return new BridgeRunInfo(job.Id, "write", false, grund, DateTime.Now);
                    }

                    var meldung = job.IstBase64
                        ? _dateiSchreibenBytes!(VollerPfad(project, job.Path!), Convert.FromBase64String(job.Content!))
                        : _dateiSchreiben(VollerPfad(project, job.Path!), job.Content!);
                    await MeldeAsync(job, "ok", meldung, 0, 0, null, cancellationToken).ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, "write", true, meldung, DateTime.Now);
                }

                default:
                {
                    await _publicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                    var abbild = await AbbildBesorgenAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Abbild noch nicht verfügbar; bitte erneut abrufen.");
                    AbbildGroesseMelden(abbild);

                    // Auf Zuruf gibt es den vollen Stand sofort – unabhängig vom Abstand oben.
                    var voll = WorkspaceManifestBuilder.ToJson(abbild);
                    await _relay.PutAsync(BridgeProtocol.ManifestDatei, voll, cancellationToken).ConfigureAwait(false);
                    await _relay.PutAsync(BridgeProtocol.ManifestLesbarDatei,
                        WorkspaceManifestBuilder.ToMarkdown(abbild), cancellationToken).ConfigureAwait(false);
                    _letztesSchwer = DateTime.Now;
                    _schwerOffen = false;
                    _letzterStand = Kurzstand(abbild);
                    await _relay.PutAsync(BridgeProtocol.ChronikDatei, _chronik.ToJson(_letzterStand), cancellationToken)
                        .ConfigureAwait(false);

                    await MeldeAsync(job, "ok", WorkspaceManifestBuilder.ToMarkdown(abbild), 0, 0, null,
                        cancellationToken).ConfigureAwait(false);
                    return new BridgeRunInfo(job.Id, art.ToString().ToLowerInvariant(), true,
                        "Vollstand abgelegt und Kurzfassung geschickt", DateTime.Now);
                    }
                    finally {_publicationGate.Release();}
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Ein bereits gesichertes Resultat darf durch einen Sendefehler nicht ersetzt werden.
            if (Journal?.Result(job.Id) is not null) throw;
            // Ein roher Windows-Satz („being used by another process") hilft niemandem; DateiFehler
            // hängt den Klartext an, wo es einen gibt.
            var meldung = DateiFehler.Aufklaren(ex);
            await MeldeAsync(job, "fehler", null, -1, 0, meldung, CancellationToken.None).ConfigureAwait(false);
            return new BridgeRunInfo(job.Id, art.ToString(), false, "Fehler: " + meldung, DateTime.Now);
        }
    }

    private Task MeldeAsync(BridgeJob job, string status, string? ausgabe, int exitCode, double dauer,
        string? hinweis, CancellationToken cancellationToken, bool redigieren = true)
    {
        var ergebnis = new BridgeResult
        {
            Id = job.Id,
            Status = status,
            Kind = job.Kind,
            // Befehlsausgaben und Protokolle werden redigiert; ein gelesener Dateiinhalt nicht
            // (TODO 8.18) – sonst kommt beim Zurückschreiben verstümmelter Code an.
            Ausgabe = ausgabe is null || !redigieren ? ausgabe : SecretRedaction.Clean(ausgabe),
            ExitCode = exitCode,
            DauerSekunden = Math.Round(dauer, 1),
            Zeit = Jetzt(),
            Hinweis = hinweis,
        };

        var json = BridgeProtocol.ToJson(ergebnis);
        Journal?.Complete(job.Id, json);
        return _relay.PutAsync(BridgeJob.ErgebnisName(job.Id), json, cancellationToken);
    }

    /// <summary>Der Projektordner – hier gesetzt, damit Aufträge darin laufen.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>
    /// Wo der Projektordner <b>in diesem Augenblick</b> liegt. Das Fenster reicht hier eine Funktion
    /// herein, weil der Ordner beim Start der Brücke noch nicht feststeht und sich ändern kann.
    /// Ohne sie gilt <see cref="ProjectRoot"/> – so arbeiten Prüfstand und Tests.
    /// </summary>
    /// <remarks>
    /// Der Grund für diese Umstellung: Bis rc7 wurde der Wert <b>einmal</b> beim Start übernommen.
    /// Zu diesem Zeitpunkt liegt die Zusammenfassung des Fensters noch nicht vor, also blieb der Wert
    /// leer – jeder Befehl endete mit „Projektordner nicht gefunden“. Gefunden am 21.09.2026 im
    /// Betrieb gegen den echten Companion, nicht im Prüfstand (der setzt den Wert selbst).
    /// </remarks>
    public Func<string>? ProjektOrdner { get; set; }

    /// <summary>Der Projektordner, wie er jetzt gilt.</summary>
    public string ProjektOrdnerJetzt()
    {
        var frisch = ProjektOrdner?.Invoke();
        return string.IsNullOrWhiteSpace(frisch) ? ProjectRoot : frisch;
    }

    /// <summary>Macht aus einem geprüften relativen Pfad den vollen Pfad im Projektordner.</summary>
    private static string VollerPfad(string project, string relativ)
        => Path.GetFullPath(Path.Combine(Path.GetFullPath(project), relativ.Replace('\\', '/')));

    /// <summary>Trägt einen ausgeführten Auftrag in den Verlauf ein (für die Anzeige).</summary>
    public void Merke(BridgeRunInfo info) => _verlauf.Add(info);

    private static BridgeStand Kurzstand(WorkspaceManifest abbild)
    {
        var git = abbild.Git;
        var gitText = git is null
            ? null
            : $"{git.Branch ?? "?"} @ {git.Head ?? "?"}";
        var inbox = abbild.Inbox.Count == 0
            ? null
            : string.Join("/", abbild.Inbox.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));

        return new BridgeStand(
            (int)abbild.Counts.GetValueOrDefault("files"),
            abbild.Counts.GetValueOrDefault("bytes"),
            abbild.Host,
            abbild.AppVersion,
            gitText,
            git?.Dirty ?? 0,
            git?.Untracked ?? 0,
            inbox);
    }

    private static string Jetzt() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>Fassung und Name – an einer Stelle, damit Protokoll und Abbild dasselbe sagen.</summary>
public static class AppInfo
{
    public const string Version = "0.2.0-rc30";
}
