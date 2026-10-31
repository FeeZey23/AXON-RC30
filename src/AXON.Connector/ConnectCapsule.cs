using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArenaDesktopCompanion.Core.Settings;

namespace AXON.Connector;

public sealed record Capsule(int Version, string Sha256, string Payload, Dictionary<string,string> Connection);
public sealed record PreparedConnect(string Id, string CapsuleJson, string Prompt);
public static class ConnectCapsule
{
    public static string RuntimeSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AXON.Connector.bridge.mjs") ?? throw new InvalidOperationException("Client fehlt.");
        using var reader = new StreamReader(stream);
        using var transport=Assembly.GetExecutingAssembly().GetManifestResourceStream("AXON.Connector.relay-http.mjs") ?? throw new InvalidOperationException("Transport fehlt.");
        using var transportReader=new StreamReader(transport);return reader.ReadToEnd().Replace("// @AXON_HTTP@",transportReader.ReadToEnd(),StringComparison.Ordinal);
    }
    public static PreparedConnect Create(Arbeitsbereich tab)
    {
        if (!Guid.TryParseExact(tab.Id,"N",out _) || !Directory.Exists(tab.ProjectFolder) || !tab.BridgeEncrypted || string.IsNullOrWhiteSpace(tab.BridgeBin) || string.IsNullOrWhiteSpace(tab.BridgeToken)) throw new InvalidOperationException("Zuerst einen gültigen Projektordner wählen.");
        ProjectRootPolicy.Validate(tab.ProjectFolder);
        var id = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes(RuntimeSource());
        using var output = new MemoryStream();
        using (var zip = new DeflateStream(output, CompressionLevel.SmallestSize, true)) zip.Write(bytes);
        var config = new Dictionary<string,string> { ["Briefkasten"]=tab.BridgeBin, ["Kennwort"]=tab.BridgeToken, ["Projektordner"]=Path.GetFullPath(tab.ProjectFolder!), ["Transport"]="aes-gcm-v1", ["SessionId"]=tab.Id, ["ConnectId"]=id };
        var json = JsonSerializer.Serialize(new Capsule(1,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),Convert.ToBase64String(output.ToArray()),config));
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var loader = "const fs=require('node:fs'),z=require('node:zlib'),c=require('node:crypto'),p=require('node:path');const x=JSON.parse(Buffer.from('"+encoded+"','base64'));if(x.Version!==1)throw Error('Kapselversion');const b=z.inflateRawSync(Buffer.from(x.Payload,'base64'),{maxOutputLength:200000});if(c.createHash('sha256').update(b).digest('hex')!==x.Sha256)throw Error('Prüfsumme');const d=p.resolve('.axon-rc30');if(fs.existsSync(d)&&(fs.lstatSync(d).isSymbolicLink()||!fs.lstatSync(d).isDirectory()))throw Error('Unsicherer Installationsordner');const cfg=p.join(d,'session.json');if(fs.existsSync(cfg)){if(fs.lstatSync(cfg).isSymbolicLink())throw Error('Unsichere Konfiguration');const old=JSON.parse(fs.readFileSync(cfg,'utf8'));if(old.SessionId!==x.Connection.SessionId)throw Error('Workspace bereits an einen anderen Tab gebunden; anderer Chat/Workspace erforderlich');}fs.mkdirSync(d,{recursive:true,mode:448});fs.chmodSync(d,448);for(const [n,v]of [['bridge.mjs',b],['session.json',JSON.stringify(x.Connection)],['.gitignore','*']]){const tmp=p.join(d,n+'.'+c.randomBytes(8).toString('hex')+'.tmp');fs.writeFileSync(tmp,v,{mode:384,flag:'wx'});fs.renameSync(tmp,p.join(d,n));}const r=require('node:child_process').spawnSync(process.execPath,[p.join(d,'bridge.mjs'),'handshake'],{stdio:'inherit'});process.exit(r.status??2);";
        var scope="AXON RC30 – verbindlicher Arbeitsort: "+Path.GetFullPath(tab.ProjectFolder!)+"\n"+
            "Das ist ein Windows-Projektordner, KEIN lokal eingebundener Arena-Workspace. Projektdateien ausschließlich über den AXON-Client in diesem Ordner lesen/schreiben; Build, npm, git und Skripte ausschließlich remote über run/vorschlag ausführen. NICHT im Arena-Workspace eine App, Landingpage, Projektkopie, Paketinstallation, einen Build oder Dev-Server anlegen. Dort sind nur .axon-rc30 und kurzlebige Transferdateien darunter erlaubt. Keine lokale Ersatzlösung bei Verbindungsfehler.\n"+
            "ZUERST ausschließlich die folgende Einrichtung ausführen. Nur bei Exit-Code 0 und bestätigt identischem Projekt fortfahren. Bei Fehler STOPP, Ursache melden, keine Projektarbeit beginnen. Ein leerer Ordner oder fehlende README.md ist erlaubt und kein Grund, anderswo zu bauen. Nach Ordnerwechsel ist die alte Kapsel ungültig: aktuellen Connect-Text neu senden. Vorhandene lokale Arena-Artefakte sind noch keine Windows-Projektdateien. Zugangsdaten niemals ausgeben.\n\n";
        var prompt=scope+"```bash\nnode <<'AXON_RC30'\n"+loader+"\nAXON_RC30\n```\n\n"+
            "Nach erfolgreicher Einrichtung nacheinander: `node .axon-rc30/bridge.mjs status` und `node .axon-rc30/bridge.mjs stand`. Das bestätigte Ziel im Abschluss kurz nennen. Danach den eigentlichen Nutzerauftrag ausschließlich in diesem Windows-Projekt bearbeiten.\n"+
            "Remote-Datei ohne lokale Projektkopie schreiben: `node .axon-rc30/bridge.mjs write relativer/pfad --stdin` mit Dateitext auf stdin (z.B. Here-Document). Lesen: `node .axon-rc30/bridge.mjs read relativer/pfad`. Remote-Programm: `node .axon-rc30/bridge.mjs vorschlag PROGRAMM ARGUMENTE...`; Arbeitsverzeichnis ist der gebundene Windows-Projektordner. Windows-Programme verwenden, keine Linux-Kommandos annehmen. Bild-/Binärdateien nur kurzzeitig unter .axon-rc30/tmp erzeugen, mit `write Zielpfad lokaleTransferdatei` übertragen und Transferdatei entfernen. Keine Zugangsdaten/Git-Aufnahme des Brückenordners.\n";

        return new(id,json,prompt);
    }
}

public enum ConnectionLight { Red, Yellow, Green }
public static class ConnectionHealth
{
    public static ConnectionLight Evaluate(bool running, string? error, bool peerConfirmed, DateTimeOffset? lastPeer, DateTimeOffset now)
        => !running || !string.IsNullOrEmpty(error) ? ConnectionLight.Red : peerConfirmed && lastPeer > now.AddMinutes(-3) ? ConnectionLight.Green : ConnectionLight.Yellow;
}

/// <summary>Provider boundary; no hidden endpoints or automated sending advertised.</summary>
public interface IChatProviderAdapter
{
    string Name { get; }
    bool SupportsAutomaticSend { get; }
    bool IsSupportedChat(Uri address);
}
public sealed class ArenaProviderAdapter : IChatProviderAdapter
{
    public string Name => "Arena Agent Mode";
    public bool SupportsAutomaticSend => false;
    public bool IsSupportedChat(Uri address) => address.Scheme=="https" && address.IsDefaultPort && address.UserInfo.Length==0 && (address.Host=="arena.ai" || address.Host=="www.arena.ai") && (address.AbsolutePath=="/agent" || address.AbsolutePath.StartsWith("/agent/",StringComparison.Ordinal));
}
