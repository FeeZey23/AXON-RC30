using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AXON.Connector;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Settings;

if(args.Contains("--public")){await PublicSmoke.RunAsync();return;}

var root=Path.Combine(Path.GetTempPath(),"axon-rc30-e2e-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
using var listener=new LoopbackRelay();var port=listener.Port;var server=listener.Completion;var files=listener.Files;
using var stop=new CancellationTokenSource();
var tabs=new List<(Arbeitsbereich Tab,BridgeService Service,string Client,string Loader)>();
for(var i=0;i<2;i++)
{
    var project=Path.Combine(root,"project"+i);var client=Path.Combine(root,"client"+i);Directory.CreateDirectory(project);Directory.CreateDirectory(client);File.WriteAllText(Path.Combine(project,"README.md"),"Projekt "+i);
    var tab=new Arbeitsbereich{ProjectFolder=project,BridgeBin="adc-e2e-rc30-"+i,BridgeToken="e2e-secret-only-"+i,BridgeEncrypted=true};var capsule=ConnectCapsule.Create(tab);tab.ConnectionId=capsule.Id;
    var relay=new RelayClient(new HttpClient(),$"http://127.0.0.1:{port}"){Bin=tab.BridgeBin,TransportSecret=tab.BridgeToken};
    string Read(string path)=>File.ReadAllText(path);
    string Write(string path,byte[] data){var relative=Path.GetRelativePath(project,path);var backup=File.Exists(path)?Path.Combine(project,".backup",Guid.NewGuid().ToString("N"),relative):null;AtomicProjectFile.WriteInside(project,relative,data,backup);return "geschrieben";}
    var service=new BridgeService(relay,new CommandRunner(),Read,(p,c)=>Write(p,Encoding.UTF8.GetBytes(c)),()=>tab.BridgeToken,()=>WorkspaceManifestBuilder.Build(project),gedaechtnis:new DateiGedaechtnis(Path.Combine(client,"done.txt")),dateiSchreibenBytes:Write){ProjectRoot=project,ConnectionId=capsule.Id,RequirePeerConfirmation=true,Journal=new BridgeJobJournal(Path.Combine(client,"journal.json")),HelloAbstand=TimeSpan.FromSeconds(1)};
    var loader=capsule.Prompt.Split("node <<'AXON_RC30'\n",StringSplitOptions.None)[1].Split("\nAXON_RC30",StringSplitOptions.None)[0];tabs.Add((tab,service,client,loader));
}
var tokens=tabs.Select(_=>new CancellationTokenSource()).ToArray();
Task Loop(int i)=>Task.Run(async()=>{var service=tabs[i].Service;try{while(!tokens[i].IsCancellationRequested){await service.TaktAsync(tokens[i].Token);await Task.Delay(100,tokens[i].Token);}}catch(OperationCanceledException){}finally{await service.WaitForPublicationAsync();}});
var loops=Enumerable.Range(0,tabs.Count).Select(Loop).ToArray();
async Task Restart(int i,bool restore)
{
 tokens[i].Cancel();await loops[i];tokens[i].Dispose();tokens[i]=new CancellationTokenSource();var old=tabs[i];var rootPath=old.Tab.ProjectFolder!;
 var relay=new RelayClient(new HttpClient(),$"http://127.0.0.1:{port}"){Bin=old.Tab.BridgeBin,TransportSecret=old.Tab.BridgeToken};
 string Write(string p,byte[] bytes){AtomicProjectFile.WriteInside(rootPath,Path.GetRelativePath(rootPath,p),bytes,null);return "geschrieben";}
 var next=new BridgeService(relay,new CommandRunner(),p=>File.ReadAllText(p),(p,c)=>Write(p,Encoding.UTF8.GetBytes(c)),()=>old.Tab.BridgeToken!,()=>WorkspaceManifestBuilder.Build(rootPath),gedaechtnis:new DateiGedaechtnis(Path.Combine(old.Client,"done.txt")),dateiSchreibenBytes:Write){ProjectRoot=rootPath,ConnectionId=old.Service.ConnectionId,RequirePeerConfirmation=true,HelloAbstand=TimeSpan.FromSeconds(1),Journal=new BridgeJobJournal(Path.Combine(old.Client,"journal.json"))};
 if(restore){old.Tab.ConnectionId=old.Service.ConnectionId;var proof=PeerResumeState.Create(old.Tab,old.Service.PeerLastSeen!.Value);if(!proof.Matches(old.Tab,DateTimeOffset.UtcNow))throw new Exception("Proof invalid");next.RestoreConfirmedPeer(old.Tab.ConnectionId,proof.LastSeen);}
 tabs[i]=(old.Tab,next,old.Client,old.Loader);loops[i]=Loop(i);await Task.Delay(1200);
}

Task<string> Node(int tab,params string[] args)=>NodeInput(tab,null,args);
async Task<string> NodeInput(int tab,string? input,params string[] args)
{
 var p=new ProcessStartInfo("node"){WorkingDirectory=tabs[tab].Client,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input is not null,UseShellExecute=false};foreach(var a in args)p.ArgumentList.Add(a);p.Environment["AXON_TEST_RELAY"]=$"http://127.0.0.1:{port}";using var child=Process.Start(p)??throw new Exception("Node fehlt");if(input is not null){await child.StandardInput.WriteAsync(input);child.StandardInput.Close();}var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));try{await child.WaitForExitAsync(timeout.Token);}catch{child.Kill(true);throw;}var result=await stdout;if(child.ExitCode!=0)throw new Exception("Node: "+await stderr);return result;
}
void Check(bool condition,string label){if(!condition)throw new Exception("FAILED: "+label);Console.WriteLine("PASS "+label);}
try
{
 await Task.WhenAll(Node(0,"-e",tabs[0].Loader),Node(1,"-e",tabs[1].Loader));Check(tabs.All(t=>t.Service.PeerConfirmed),"Zwei selbstenthaltene Kapseln gleichzeitig installiert, Handshake+ACK pro Projekt");
 var read=await Task.WhenAll(Node(0,".axon-rc30/bridge.mjs","read","README.md"),Node(1,".axon-rc30/bridge.mjs","read","README.md"));Check(read[0].Trim()=="Projekt 0"&&read[1].Trim()=="Projekt 1","Parallele Clients lesen ausschließlich ihr eigenes Projekt");
 await NodeInput(0,"Direkt nach Windows/Projekt · äöü\n", ".axon-rc30/bridge.mjs","write","direct.txt","--stdin");Check(File.ReadAllText(Path.Combine(tabs[0].Tab.ProjectFolder!,"direct.txt"))=="Direkt nach Windows/Projekt · äöü\n"&&!File.Exists(Path.Combine(tabs[0].Client,"direct.txt")),"stdin schreibt ausschließlich ins gebundene Projekt, ohne lokale Projektdatei");
 File.Delete(Path.Combine(tabs[0].Client,".axon-rc30","binding.json"));await Node(0,".axon-rc30/bridge.mjs","read","direct.txt");Check(File.Exists(Path.Combine(tabs[0].Client,".axon-rc30","binding.json")),"Fehlende Bindungsbestätigung wird vor Projektaktion neu eingeholt");
 var beforeStatus=tabs[0].Service.Verlauf.Count;var status=await Node(0,".axon-rc30/bridge.mjs","status");Check(status.Contains("Frischer Hin-",StringComparison.Ordinal)&&tabs[0].Service.Verlauf.Count>=beforeStatus+2,"Status führt echten Connect/ACK-Rundlauf aus statt nur Hello zu lesen");
 beforeStatus=tabs[0].Service.Verlauf.Count;var transport=await Node(0,".axon-rc30/bridge.mjs","status","--transport-only");Check(transport.Contains("transport_only",StringComparison.Ordinal)&&tabs[0].Service.Verlauf.Count==beforeStatus,"Expliziter Transportstatus behauptet keine Chatbestätigung und erzeugt keinen ACK");
 var local=Path.Combine(tabs[0].Client,"input.txt");File.WriteAllText(local,"Version 1 äöü");await Node(0,".axon-rc30/bridge.mjs","write","Scripts/probe.txt",local);Check((await Node(0,".axon-rc30/bridge.mjs","read","Scripts/probe.txt")).Trim()=="Version 1 äöü","Node→C# Schreiben/Lesen mit Unicode");
 File.WriteAllText(local,"Version 2");await Node(0,".axon-rc30/bridge.mjs","write","Scripts/probe.txt",local);Check(File.ReadAllText(Path.Combine(tabs[0].Tab.ProjectFolder!,"Scripts","probe.txt"))=="Version 2","Überschreiben mit neuer Version");Check(Directory.EnumerateFiles(Path.Combine(tabs[0].Tab.ProjectFolder!,".backup"),"probe.txt",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="Version 1 äöü"),"Vorversion vor Überschreiben gesichert");Check(!File.Exists(Path.Combine(tabs[1].Tab.ProjectFolder!,"Scripts","probe.txt")),"Keine Dateiänderung im anderen Projekt");
 files[tabs[0].Tab.BridgeBin+"/job-aaa-poison.json"]="kaputt";await Node(0,".axon-rc30/bridge.mjs","ping");Check(!files.ContainsKey(tabs[0].Tab.BridgeBin+"/job-aaa-poison.json"),"Manipulierter Auftrag blockiert echten C#↔Node-Ping nicht");
 var prepared=ConnectCapsule.Create(tabs[0].Tab);tabs[0].Service.ConnectionId=prepared.Id;Check(!tabs[0].Service.PeerConfirmed,"Neuer Connect verwirft alten grünen Status");await tabs[0].Service.WaitForPublicationAsync();await Task.Delay(1200);await tabs[0].Service.TaktAsync();await tabs[0].Service.WaitForPublicationAsync();try{await NodeInput(0,"stale must not write",".axon-rc30/bridge.mjs","write","stale.txt","--stdin");throw new Exception("Alte Bindung akzeptiert");}catch(Exception ex)when(ex.Message.Contains("Bindung geändert",StringComparison.Ordinal)){}Check(!File.Exists(Path.Combine(tabs[0].Tab.ProjectFolder!,"stale.txt")),"Veraltete Connect-ID sperrt Schreiben vor Auftragserstellung");var loader=prepared.Prompt.Split("node <<'AXON_RC30'\n")[1].Split("\nAXON_RC30")[0];await Node(0,"-e",loader);Check(tabs[0].Service.PeerConfirmed&&tabs[1].Service.PeerConfirmed,"Wiederverbinden eines Clients unterbricht den anderen nicht");
 var manifest=await Node(0,".axon-rc30/bridge.mjs","stand");Check(manifest.Contains("README.md",StringComparison.Ordinal),"Explizites Node-Manifest wird asynchron vom echten C#-Dienst beantwortet");
 await Restart(0,true);Check(tabs[0].Service.PeerConfirmed&&tabs[0].Service.PeerRestored,"Host-Neustart lädt signierte echte Bindung ohne erfundenen frischen Kontakt");
 var resumed=await NodeInput(0,"nach Neustart", ".axon-rc30/bridge.mjs","write","resumed.txt","--stdin");Check(File.ReadAllText(Path.Combine(tabs[0].Tab.ProjectFolder!,"resumed.txt"))=="nach Neustart"&&!tabs[0].Service.PeerRestored&&!resumed.Contains("\"status\":\"verbunden\"",StringComparison.Ordinal),"Gleicher Cloud-Client arbeitet nach Host-Neustart ohne neue Kapsel/Handshake weiter");
 await Restart(0,false);var automatic=await Node(0,".axon-rc30/bridge.mjs","read","resumed.txt");Check(tabs[0].Service.PeerConfirmed&&automatic.Contains("nach Neustart",StringComparison.Ordinal),"Fehlender Bestandsbeleg führt automatisch zum Handshake, ohne manuelles Connect-Senden");
 Check(!files.Keys.Any(k=>k.Contains("/job-",StringComparison.Ordinal)||k.Contains("/result-",StringComparison.Ordinal)),"Aufträge und Antworten nach Erfolg aufgeräumt");
 var configPath=Path.Combine(tabs[0].Client,".axon-rc30","session.json");var savedConfig=File.ReadAllText(configPath);
 var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(prepared.CapsuleJson));var bad=JsonSerializer.Deserialize<Capsule>(prepared.CapsuleJson)! with{Sha256=new string('0',64)};
 try{await Node(0,"-e",loader.Replace(encoded,Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bad))),StringComparison.Ordinal));throw new Exception("Ungültige Prüfsumme akzeptiert");}catch(Exception ex)when(ex.Message.Contains("Prüfsumme",StringComparison.Ordinal)&&ex.Message.StartsWith("Node:",StringComparison.Ordinal)){}
 Check(File.ReadAllText(configPath)==savedConfig,"Manipulierte Kapsel wird vor Konfigurationsänderung verworfen");
 try{await Node(0,"-e",tabs[1].Loader);throw new Exception("Fremde Sitzung akzeptiert");}catch(Exception ex)when(ex.Message.Contains("anderen Tab",StringComparison.Ordinal)&&ex.Message.StartsWith("Node:",StringComparison.Ordinal)){}
 Check(File.ReadAllText(configPath)==savedConfig,"Zweite Tab-Sitzung darf bestehende Workspace-Bindung nicht überschreiben");
 Check(File.ReadAllText(Path.Combine(tabs[0].Client,".axon-rc30",".gitignore")).Trim()=="*","Installierte Zugangsdaten werden durch eigene Gitignore ausgeschlossen");
 tokens[0].Cancel();await loops[0];
 var reachable=await Node(0,".axon-rc30/bridge.mjs","status","--transport-only");
 try{await Node(0,".axon-rc30/bridge.mjs","status");throw new Exception("Status meldet trotz fehlender Host-Antwort Erfolg");}catch(Exception ex)when(ex.Message.StartsWith("Node:",StringComparison.Ordinal)&&ex.Message.Contains("Keine Antwort",StringComparison.Ordinal)){}
 Check(reachable.Contains("transport_only",StringComparison.Ordinal),"Lesbares Hello ohne Job-Antwort führt nicht zu grünem frischen Status");

 Console.WriteLine("RC30 E2E PASSED");
}
finally{stop.Cancel();foreach(var t in tokens)t.Cancel();listener.Stop();await Task.WhenAll(loops);foreach(var t in tokens)t.Dispose();await server;Directory.Delete(root,true);}

// Socket-based loopback server avoids Windows HTTP.sys URLACL/admin requirements.
sealed class LoopbackRelay : IDisposable
{
    private readonly TcpListener _listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource _stop=new();
    public ConcurrentDictionary<string,string> Files {get;}=new();
    public int Port {get;}
    public Task Completion {get;}
    public LoopbackRelay(){_listener.Start();Port=((IPEndPoint)_listener.LocalEndpoint).Port;Completion=AcceptAsync();}
    private async Task AcceptAsync()
    {
        try{while(!_stop.IsCancellationRequested){var client=await _listener.AcceptTcpClientAsync(_stop.Token);_ = ServeAsync(client);}}
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
        catch(SocketException)when(_stop.IsCancellationRequested){}
    }
    private async Task ServeAsync(TcpClient client)
    {
        using(client)
        {
            try
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(20));var token=deadline.Token;
                var stream=client.GetStream();using var header=new MemoryStream();var one=new byte[1];
                while(header.Length<32768){if(await stream.ReadAsync(one,token)==0)return;header.WriteByte(one[0]);if(header.Length>=4&&header.GetBuffer().AsSpan((int)header.Length-4,4).SequenceEqual("\r\n\r\n"u8))break;}
                var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var request=lines[0].Split(' ');var method=request[0];var rel=Uri.UnescapeDataString(request[1].Split('?')[0]).Trim('/');
                var lengthHeader=lines.FirstOrDefault(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));var count=lengthHeader is null?0:int.Parse(lengthHeader[(lengthHeader.IndexOf(':')+1)..]);if(count<0||count>8*1024*1024)throw new IOException("Request too large");var body=new byte[count];await stream.ReadExactlyAsync(body,token);
                var status=200;string data="";
                if(method=="PUT"){Files[rel]=Encoding.UTF8.GetString(body);status=201;}
                else if(method=="DELETE"){Files.TryRemove(rel,out _);status=204;}
                else if(!rel.Contains('/'))data=JsonSerializer.Serialize(new{files=Files.Where(x=>x.Key.StartsWith(rel+"/",StringComparison.Ordinal)).Select(x=>new{filename=x.Key[(rel.Length+1)..],bytes=x.Value.Length})});
                else if(Files.TryGetValue(rel,out var found))data=found;else status=404;
                var bytes=Encoding.UTF8.GetBytes(data);var response=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");await stream.WriteAsync(response,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
            }
            catch(Exception ex)when(ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException){}
        }
    }
    public void Stop(){_stop.Cancel();_listener.Stop();}
    public void Dispose(){Stop();_stop.Dispose();}
}
