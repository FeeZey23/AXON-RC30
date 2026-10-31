using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AXON.Connector;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Settings;

// Independent disposable mailbox. Never touches the user's RC30 tab, pairing, or project.
static class PublicSmoke
{
    public static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"RC30","qa",Guid.NewGuid().ToString("N"));
        var project=Path.Combine(root,"project");var client=Path.Combine(root,"client");Directory.CreateDirectory(project);Directory.CreateDirectory(client);
        using var http=new HttpClient();using var stop=new CancellationTokenSource();
        var tab=new Arbeitsbereich{ProjectFolder=project,BridgeBin=RelayClient.NeuerName(),BridgeToken=RelayClient.NeuesKennwort(),BridgeEncrypted=true};
        var capsule=ConnectCapsule.Create(tab);tab.ConnectionId=capsule.Id;
        var relay=new RelayClient(http){Bin=tab.BridgeBin,TransportSecret=tab.BridgeToken};
        string Write(string p,byte[] bytes){AtomicProjectFile.WriteInside(project,Path.GetRelativePath(project,p),bytes,null);return "geschrieben";}
        var service=new BridgeService(relay,new CommandRunner(),p=>File.Exists(p)?File.ReadAllText(p):null,(p,c)=>Write(p,Encoding.UTF8.GetBytes(c)),()=>tab.BridgeToken,()=>WorkspaceManifestBuilder.Build(project),dateiSchreibenBytes:Write){ProjectRoot=project,ConnectionId=capsule.Id,RequirePeerConfirmation=true,HelloAbstand=TimeSpan.FromSeconds(20)};
        var loop=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){await service.TaktAsync(stop.Token);await Task.Delay(500,stop.Token);}}catch(OperationCanceledException){}finally{await service.WaitForPublicationAsync();}});
        async Task<string> Node(string? input,params string[] args)
        {
            var p=new ProcessStartInfo("node"){WorkingDirectory=client,RedirectStandardInput=input is not null,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};foreach(var a in args)p.ArgumentList.Add(a);p.Environment.Remove("AXON_TEST_RELAY");
            using var child=Process.Start(p)??throw new Exception("Node fehlt");var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();if(input is not null){await child.StandardInput.WriteAsync(input);child.StandardInput.Close();}
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(180));try{await child.WaitForExitAsync(timeout.Token);}catch{child.Kill(true);throw;}if(child.ExitCode!=0)throw new Exception("Public Node: "+await stderr);return await stdout;
        }
        void Check(bool value,string name){if(!value)throw new Exception("PUBLIC FAILED: "+name);Console.WriteLine("PUBLIC PASS "+name);}
        try
        {
            var loader=capsule.Prompt.Split("node <<'AXON_RC30'\n")[1].Split("\nAXON_RC30")[0];await Node(null,"-e",loader);Check(service.PeerConfirmed,"Public Filebin roundtrip handshake + exact ACK (independent test session)");
            const string content="Remote-only transfer · äöü\n";await Node(content,".axon-rc30/bridge.mjs","write","public-probe.txt","--stdin");Check(File.ReadAllText(Path.Combine(project,"public-probe.txt"))==content,"stdin reaches C# project file byte-exactly");
            var read=await Node(null,".axon-rc30/bridge.mjs","read","public-probe.txt");Check(read.TrimEnd()==content.TrimEnd(),"public remote read returns written Unicode content");
            Check(Directory.EnumerateFileSystemEntries(client).All(p=>Path.GetFileName(p)==".axon-rc30"),"client contains only bridge bootstrap; no local project clone/build");
            Console.WriteLine("PUBLIC E2E PASSED (synthetic sandbox C# host, NOT the selected Windows user tab)");
        }
        finally
        {
            stop.Cancel();await loop;
            try{foreach(var file in await relay.ListAsync())await relay.DeleteAsync(file.Name);}catch(HttpRequestException){Console.WriteLine("PUBLIC cleanup incomplete; disposable encrypted test mailbox expires automatically");}
            Directory.Delete(root,true);
        }
    }
}
