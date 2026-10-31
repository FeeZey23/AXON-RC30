using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Settings;

namespace ArenaDesktopCompanion.Core.Tests;

public sealed class BridgeUpdateTests
{
    private const string Secret = "abcd-efgh-ijkl-mnop";
    private const string Bin = "adc-vector123";

    [Fact]
    public void Python_AesGcm_Vektor_ist_kompatibel()
    {
        const string vector = "{\"v\":1,\"nonce\":\"AAECAwQFBgcICQoL\",\"data\":\"K8eVgq3lSsePjK7vwfUY7Bx5kO29YGhePM9EHLBL8Sz3EmNhmgpIPoDqSZS1cKfX3WwjmarlE0HvKAil\"}";
        Assert.Equal("Hallo AXON ✓", BridgeCipher.Decrypt(Bin, "hello.json", vector, Secret));
    }

    [Theory]
    [InlineData("anderes-geheimnis", Bin, "job-one.json")]
    [InlineData(Secret, "adc-other123", "job-one.json")]
    [InlineData(Secret, Bin, "job-two.json")]
    public void Verschluesselung_bindet_Schluessel_Briefkasten_und_Datei(string key, string bin, string file)
    {
        var cipher = BridgeCipher.Encrypt(Bin, "job-one.json", "test", Secret);
        Assert.ThrowsAny<CryptographicException>(() => BridgeCipher.Decrypt(bin, file, cipher, key));
    }

    [Fact]
    public void Kein_Klartext_Downgrade_und_keine_Wiederholung_alter_Auftraege()
    {
        Assert.ThrowsAny<CryptographicException>(() => BridgeCipher.Decrypt(Bin, "job-one.json", "{}", Secret));
        var old = BridgeCipher.Encrypt(Bin, "job-one.json", "test", Secret, DateTimeOffset.UtcNow.AddHours(-1));
        Assert.ThrowsAny<CryptographicException>(() => BridgeCipher.Decrypt(Bin, "job-one.json", old, Secret));
        var future = BridgeCipher.Encrypt(Bin, "job-one.json", "test", Secret, DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.ThrowsAny<CryptographicException>(() => BridgeCipher.Decrypt(Bin, "job-one.json", future, Secret));
    }

    [Fact]
    public void Zufallsnonce_und_Unicode_Rundreise()
    {
        var one = BridgeCipher.Encrypt(Bin, "workspace.md", "Übersicht ✓", Secret);
        var two = BridgeCipher.Encrypt(Bin, "workspace.md", "Übersicht ✓", Secret);
        Assert.NotEqual(one, two);
        Assert.DoesNotContain("Übersicht", one);
        Assert.DoesNotContain(Secret, one);
        Assert.Equal("Übersicht ✓", BridgeCipher.Decrypt(Bin, "workspace.md", one, Secret));
    }

    [Fact]
    public void Sicherungsfehler_verhindert_Ueberschreiben()
    {
        var root = Directory.CreateTempSubdirectory("axon-backup-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "a.txt"), "alt");
            File.WriteAllText(Path.Combine(root.FullName, "blockiert"), "kein Ordner");
            Assert.ThrowsAny<IOException>(() => AtomicProjectFile.WriteInside(root.FullName, "a.txt", Encoding.UTF8.GetBytes("neu"), Path.Combine(root.FullName, "blockiert", "a.txt")));
            Assert.Equal("alt", File.ReadAllText(Path.Combine(root.FullName, "a.txt")));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Atomare_Uebernahme_sichert_die_Vorversion_und_hinterlaesst_keine_Temps()
    {
        var root = Directory.CreateTempSubdirectory("axon-write-");
        try
        {
            var target = Path.Combine(root.FullName, "a.txt");
            var backup = Path.Combine(root.FullName, "Backup", "snapshot", "a.txt");
            File.WriteAllText(target, "alt");
            AtomicProjectFile.WriteInside(root.FullName, "a.txt", Encoding.UTF8.GetBytes("neu"), backup);
            Assert.Equal("alt", File.ReadAllText(backup));
            Assert.Equal("neu", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(root.FullName, "*.tmp", SearchOption.AllDirectories));
            Assert.ThrowsAny<IOException>(() => AtomicProjectFile.WriteInside(root.FullName, "a.txt", Encoding.UTF8.GetBytes("kaputt"), backup));
            Assert.Equal("neu", File.ReadAllText(target));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Journal_bewahrt_Ergebnisse_ueber_Neustart()
    {
        var root = Directory.CreateTempSubdirectory("axon-journal-");
        try
        {
            var path = Path.Combine(root.FullName, "journal.json");
            var journal = new BridgeJobJournal(path);
            journal.Begin("one");
            Assert.Equal("läuft", new BridgeJobJournal(path).State("one"));
            journal.Complete("one", "{\"status\":\"ok\"}");
            Assert.Equal("{\"status\":\"ok\"}", new BridgeJobJournal(path).Result("one"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Persistenzfehler_verhindert_falsche_Annahme()
    {
        var root = Directory.CreateTempSubdirectory("axon-memory-");
        try
        {
            var blocker = Path.Combine(root.FullName, "file");
            File.WriteAllText(blocker, "blockiert");
            var memory = new DateiGedaechtnis(Path.Combine(blocker, "memory.txt"));
            Assert.ThrowsAny<IOException>(() => memory.Merke("one"));
            Assert.False(memory.Kenne("one"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task Http500_ist_keine_erfolgreiche_Uebertragung()
    {
        using var relay = new RelayClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)))) { Bin = Bin };
        await Assert.ThrowsAsync<HttpRequestException>(() => relay.PutAsync("hello.json", "{}"));
        await Assert.ThrowsAsync<HttpRequestException>(() => relay.GetAsync("hello.json"));
        await Assert.ThrowsAsync<HttpRequestException>(() => relay.ListAsync());
    }

    [Fact]
    public async Task Zu_grosse_Antwort_wird_abgelehnt()
    {
        using var relay = new RelayClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[6 * 1024 * 1024 + 1]) }))) { Bin = Bin };
        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.GetAsync("job-one.json"));
    }

    [Fact]
    public async Task Ergebnis_wird_nach_Netzfehler_wiederholt_aber_Aktion_nicht()
    {
        var root = Directory.CreateTempSubdirectory("axon-outbox-");
        try
        {
            var job = BridgeProtocol.ToJson(new BridgeJob { Id = "one", Kind = "write", Path = "a.txt", Content = "neu", Token = Secret });
            var writes = 0;
            var resultAttempts = 0;
            var result = "";
            var list = "{\"files\":[{\"filename\":\"job-one.json\",\"bytes\":100}]}";
            using var relay = new RelayClient(new HttpClient(new Handler(req =>
            {
                var name = req.RequestUri!.AbsolutePath.Split('/').Last();
                if (req.Method == HttpMethod.Put)
                {
                    if (name == "result-one.json")
                    {
                        resultAttempts++;
                        if (resultAttempts == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        result = new StreamReader(req.Content!.ReadAsStream()).ReadToEnd();
                    }
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(name == "job-one.json" ? job : list) };
            }))) { Bin = Bin };
            var journal = new BridgeJobJournal(Path.Combine(root.FullName, "journal.json"));
            var service = new BridgeService(relay, new CommandRunner(), _ => null, (_, _) => { writes++; return "geschrieben"; }, () => Secret,
                () => WorkspaceManifestBuilder.Build(root.FullName, "test", AppInfo.Version))
            { ProjectRoot = root.FullName, SchreibendErlaubt = true, Journal = journal };
            await service.TaktAsync();
            await service.WaitForPublicationAsync();
            await service.TaktAsync();
            await service.WaitForPublicationAsync();
            Assert.Equal(1, writes);
            Assert.True(resultAttempts >= 2);
            Assert.Equal("ok", BridgeProtocol.FromJson<BridgeResult>(result)!.Status);
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("id/slash")]
    [InlineData("a.b")]
    public void Unsichere_Auftragskennungen_werden_abgelehnt(string id)
    {
        var (ok, _) = BridgeProtocol.Pruefe(new BridgeJob { Id = id, Kind = "ping", Token = Secret }, Secret, "C:\\project", true, true, true);
        Assert.False(ok);
    }

    [Fact]
    public void Falsches_Projekt_wird_abgelehnt()
    {
        var (ok, _) = BridgeProtocol.Pruefe(new BridgeJob { Id = "one", Kind = "ping", Token = Secret, Projekt = "C:\\other" }, Secret, "C:\\project", true, true, true);
        Assert.False(ok);
    }

    [Fact]
    public void Neue_Tabs_sind_verschluesselt_Altbestand_wird_nicht_still_umgestellt()
    {
        var settings = new AppSettings { BridgeBin = Bin, BridgeToken = Secret };
        settings.ArbeitsbereicheSicherstellen();
        Assert.False(settings.Arbeitsbereiche[0].BridgeEncrypted);
        var index = settings.ArbeitsbereichAnlegen();
        Assert.True(settings.Arbeitsbereiche[index].BridgeEncrypted);
    }

    [Fact]
    public void Kurzes_Briefing_enthaelt_Pruefsumme_und_vollstaendige_Verbindung_ohne_grossen_Installer()
    {
        using var t=new ArenaDesktopCompanion.Core.Tests.Support.TempTree();
        var prepared=AXON.Connector.ConnectCapsule.Create(new ArenaDesktopCompanion.Core.Settings.Arbeitsbereich{ProjectFolder=t.Root,BridgeBin=Bin,BridgeToken=Secret,BridgeEncrypted=true});
        Assert.Contains("sha256",prepared.Prompt);
        Assert.Contains("inflateRawSync",prepared.Prompt);
        Assert.DoesNotContain("pip install",prepared.Prompt);
        Assert.True(prepared.Prompt.Length<12000);

    }

    [Fact]
    public void Symlink_Dateipfad_wird_blockiert()
    {
        if (OperatingSystem.IsWindows()) return; // Windows-Junction-Klickabnahme separat, benötigt Rechte.
        var root = Directory.CreateTempSubdirectory("axon-link-");
        var outside = Directory.CreateTempSubdirectory("axon-outside-");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root.FullName, "link"), outside.FullName);
            Assert.False(BridgeProtocol.SichererPfad(root.FullName, "link/a.txt", out _));
            Assert.Throws<InvalidOperationException>(() => AtomicProjectFile.WriteInside(root.FullName, "link/a.txt", Encoding.UTF8.GetBytes("no"), null));
            Assert.False(File.Exists(Path.Combine(outside.FullName, "a.txt")));
        }
        finally { root.Delete(true); outside.Delete(true); }
    }

    [Fact]
    public async Task Verschluesselter_Ping_ohne_Kennwort_im_Auftrag_wird_bestaetigt()
    {
        var root = Directory.CreateTempSubdirectory("axon-ping-");
        try
        {
            var job = BridgeProtocol.ToJson(new BridgeJob { Id = "one", Kind = "ping", Projekt = root.FullName });
            var wire = BridgeCipher.Encrypt(Bin, "job-one.json", job, Secret);
            var result = "";
            using var relay = new RelayClient(new HttpClient(new Handler(req =>
            {
                var name = req.RequestUri!.AbsolutePath.Split('/').Last();
                if (req.Method == HttpMethod.Put)
                {
                    if (name == "result-one.json") result = new StreamReader(req.Content!.ReadAsStream()).ReadToEnd();
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(name == "job-one.json" ? wire : "{\"files\":[{\"filename\":\"job-one.json\",\"bytes\":300}]}") };
            }))) { Bin = Bin, TransportSecret = Secret };
            var service = new BridgeService(relay, new CommandRunner(), _ => throw new Exception("Ping darf nicht lesen"), (_, _) => throw new Exception("Ping darf nicht schreiben"), () => Secret,
                () => WorkspaceManifestBuilder.Build(root.FullName, "test", AppInfo.Version)) { ProjectRoot = root.FullName };
            await service.TaktAsync();
            await service.WaitForPublicationAsync();
            var decoded = BridgeProtocol.FromJson<BridgeResult>(BridgeCipher.Decrypt(Bin, "result-one.json", result, Secret));
            Assert.Equal("ok", decoded!.Status);
            Assert.Equal(root.FullName, decoded.Ausgabe);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Abgeholte_Ergebnisse_belegen_keinen_unbegrenzten_Puffer()
    {
        var root = Directory.CreateTempSubdirectory("axon-ack-");
        try
        {
            var path = Path.Combine(root.FullName, "journal.json");
            var journal = new BridgeJobJournal(path);
            journal.Begin("one");
            journal.Complete("one", new string('x', 100000));
            journal.Reconcile(new HashSet<string>(StringComparer.Ordinal) { "one" });
            Assert.NotNull(journal.Result("one"));
            journal.Reconcile(new HashSet<string>(StringComparer.Ordinal));
            var restored = new BridgeJobJournal(path);
            Assert.Equal("zugestellt", restored.State("one"));
            Assert.Null(restored.Result("one"));
            Assert.True(new FileInfo(path).Length < 1000);
        }
        finally { root.Delete(true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handle(request));
    }
}
