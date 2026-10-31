using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AXON.Connector;
using ArenaDesktopCompanion.Core.Agents;
using ArenaDesktopCompanion.Core.Bridge;
using ArenaDesktopCompanion.Core.Settings;
using ArenaDesktopCompanion.Core.Tests.Support;
namespace ArenaDesktopCompanion.Core.Tests;

public class Rc30Tests
{
    [Fact] public void New_tabs_get_distinct_ids_profiles_and_mailboxes(){var s=new AppSettings();s.ArbeitsbereichAnlegen();s.ArbeitsbereichAnlegen();Assert.NotEqual(s.Arbeitsbereiche[0].Id,s.Arbeitsbereiche[1].Id);Assert.NotEqual(Rc30Paths.ProfileFolder(s.Arbeitsbereiche[0].Id),Rc30Paths.ProfileFolder(s.Arbeitsbereiche[1].Id));Assert.NotEqual(s.Arbeitsbereiche[0].BridgeBin,s.Arbeitsbereiche[1].BridgeBin);}
    [Fact] public void Invalid_and_duplicate_ids_are_repaired(){var s=new AppSettings();s.ArbeitsbereichAnlegen();s.ArbeitsbereichAnlegen();s.Arbeitsbereiche[1].Id=s.Arbeitsbereiche[0].Id;s.ArbeitsbereicheSicherstellen();Assert.Equal(2,s.Arbeitsbereiche.Select(x=>x.Id).Distinct().Count());s.Arbeitsbereiche[0].Id="../bad";s.ArbeitsbereicheSicherstellen();Assert.True(Guid.TryParseExact(s.Arbeitsbereiche[0].Id,"N",out _));}
    [Theory][InlineData("../outside")][InlineData("a/b")][InlineData("")][InlineData("abc")]
    public void Profile_paths_reject_non_ids(string id)=>Assert.Throws<ArgumentException>(()=>Rc30Paths.ProfileFolder(id));
    [Fact] public void Reordering_preserves_active_identity_and_bindings(){var s=new AppSettings();for(var i=0;i<3;i++)s.ArbeitsbereichAnlegen();s.AktiverArbeitsbereich=1;var active=s.Arbeitsbereiche[1];var bin=active.BridgeBin;Assert.True(TabOrder.Move(s,s.Arbeitsbereiche[0].Id,s.Arbeitsbereiche[2].Id));Assert.Same(active,s.Arbeitsbereiche[s.AktiverArbeitsbereich]);Assert.Equal(bin,active.BridgeBin);}
    [Fact] public void Reordering_unknown_tab_is_noop(){var s=new AppSettings();s.ArbeitsbereichAnlegen();Assert.False(TabOrder.Move(s,"missing",s.Arbeitsbereiche[0].Id));}
    [Fact] public void Groups_and_panel_layout_roundtrip(){var s=new AppSettings();s.ArbeitsbereichAnlegen();var t=s.Arbeitsbereiche[0];t.Group="Arbeit";t.Panels.Add(new(){Kind="logs",Height=444,Detached=true});var r=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;Assert.Equal(t.Id,r.Arbeitsbereiche[0].Id);Assert.Equal("Arbeit",r.Arbeitsbereiche[0].Group);Assert.True(r.Arbeitsbereiche[0].Panels[0].Detached);Assert.Equal(444,r.Arbeitsbereiche[0].Panels[0].Height);}
    [Theory][InlineData("a/b/c.dat","..\\b\\c.dat")][InlineData("Scripts\\Config.dat","..\\Scripts\\Config.dat")][InlineData("one.txt","one.txt")]
    public void Event_paths_show_parent_and_filename(string input,string expected)=>Assert.Equal(expected,TabOrder.ShortPath(input));
    [Theory][InlineData(false,null,false,ConnectionLight.Red)][InlineData(true,"offline",true,ConnectionLight.Red)][InlineData(true,null,false,ConnectionLight.Yellow)][InlineData(true,null,true,ConnectionLight.Green)]
    public void Green_requires_peer_proof(bool running,string? error,bool peer,ConnectionLight expected)=>Assert.Equal(expected,ConnectionHealth.Evaluate(running,error,peer,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
    [Fact] public void Old_peer_proof_expires()=>Assert.Equal(ConnectionLight.Yellow,ConnectionHealth.Evaluate(true,null,true,DateTimeOffset.UtcNow.AddMinutes(-4),DateTimeOffset.UtcNow));
    [Fact] public void No_peer_timestamp_is_not_green()=>Assert.Equal(ConnectionLight.Yellow,ConnectionHealth.Evaluate(true,null,true,null,DateTimeOffset.UtcNow));
    [Theory][InlineData("https://arena.ai/agent",true)][InlineData("https://arena.ai/agent/chat/123",true)][InlineData("https://arena.ai/",false)][InlineData("https://arena.ai.evil.test/agent",false)][InlineData("http://arena.ai/agent",false)][InlineData("https://arena.ai/agent-wrong",false)][InlineData("https://arena.ai:8443/agent",false)][InlineData("https://arena.ai/agent?model=x",true)]
    public void Provider_origin_and_mode_are_checked(string address,bool expected)=>Assert.Equal(expected,new ArenaProviderAdapter().IsSupportedChat(new Uri(address)));
    [Fact] public void Provider_does_not_falsely_offer_automatic_send()=>Assert.False(new ArenaProviderAdapter().SupportsAutomaticSend);
    [Fact] public void Capsule_contains_complete_pinned_runtime_and_session(){using var t=new TempTree();var tab=Tab(t.Root);var c=ConnectCapsule.Create(tab);var capsule=JsonSerializer.Deserialize<Capsule>(c.CapsuleJson)!;using var input=new MemoryStream(Convert.FromBase64String(capsule.Payload));using var zip=new DeflateStream(input,CompressionMode.Decompress);using var bytes=new MemoryStream();zip.CopyTo(bytes);Assert.Equal(capsule.Sha256,Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());Assert.Equal(ConnectCapsule.RuntimeSource(),Encoding.UTF8.GetString(bytes.ToArray()));Assert.Equal(tab.Id,capsule.Connection["SessionId"]);Assert.Equal(c.Id,capsule.Connection["ConnectId"]);Assert.Contains("node <<'AXON_RC30'",c.Prompt);Assert.DoesNotContain("pip install",c.Prompt);Assert.DoesNotContain("filebin.net/",c.Prompt);}
    [Fact] public void Each_connect_has_unique_challenge(){using var t=new TempTree();var tab=Tab(t.Root);Assert.NotEqual(ConnectCapsule.Create(tab).Id,ConnectCapsule.Create(tab).Id);}
    [Fact] public void Capsule_refuses_missing_root()=>Assert.Throws<InvalidOperationException>(()=>ConnectCapsule.Create(Tab(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()))));
    [Fact] public void Capsule_refuses_cleartext(){using var t=new TempTree();var tab=Tab(t.Root);tab.BridgeEncrypted=false;Assert.Throws<InvalidOperationException>(()=>ConnectCapsule.Create(tab));}
    private static Arbeitsbereich Tab(string root)=>new(){ProjectFolder=root,BridgeBin="adc-rc30test12345",BridgeToken="test-secret-only-1234",BridgeEncrypted=true};

    private sealed class Relay : HttpMessageHandler
    {
        public ConcurrentDictionary<string,string> Files {get;}=new();public bool FailManifest;public bool StallManifest;public Task? ManifestGate;public TaskCompletionSource? ManifestStarted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
        {
            var name=r.RequestUri!.Segments.Last().Trim('/');
            if(r.Method==HttpMethod.Put){if(name=="manifest.json"&&ManifestGate is not null){ManifestStarted?.TrySetResult();await ManifestGate.WaitAsync(token);}if(name=="manifest.json"&&StallManifest)await Task.Delay(10000,token);if(name=="manifest.json"&&FailManifest)return new(HttpStatusCode.ServiceUnavailable);Files[name]=await r.Content!.ReadAsStringAsync(token);return new(HttpStatusCode.Created);}
            if(r.Method==HttpMethod.Delete){Files.TryRemove(name,out _);return new(HttpStatusCode.NoContent);}
            if(name=="adc-rc30test12345")return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{files=Files.Keys.OrderBy(x=>x).Select(filename=>new{filename,bytes=0})}))};
            return Files.TryGetValue(name,out var text)?new(HttpStatusCode.OK){Content=new StringContent(text)}:new(HttpStatusCode.NotFound);
        }
    }
    private static BridgeService Service(Relay relay,string root,bool secure=true,bool require=false)
    {
        var client=new RelayClient(new HttpClient(relay)){Bin="adc-rc30test12345",TransportSecret=secure?"test-secret-only-1234":null};
        return new(client,new CommandRunner(),p=>File.Exists(p)?File.ReadAllText(p):null,(p,c)=>{File.WriteAllText(p,c);return "ok";},()=>"test-secret-only-1234",()=>WorkspaceManifestBuilder.Build(root)){ProjectRoot=root,ConnectionId="challenge-1",RequirePeerConfirmation=require};
    }
    private static void Queue(Relay relay,string root,string id,string? note=null,string? secret=null){var name="job-"+id+".json";relay.Files[name]=BridgeCipher.Encrypt("adc-rc30test12345",name,BridgeProtocol.ToJson(new BridgeJob{Id=id,Kind="ping",Projekt=root,Note=note}),secret??"test-secret-only-1234");}
    [Theory][InlineData(".arena-inbox")][InlineData(".ARENA-INBOX")][InlineData(".arena-inbox/nested")][InlineData(".arena-backup")][InlineData(".arena-conflicts")]
    public void Internal_folders_cannot_become_project_roots(string relative){using var t=new TempTree();var dir=t.Full(relative);Directory.CreateDirectory(dir);Assert.Throws<InvalidOperationException>(()=>ProjectRootPolicy.Validate(dir));}
    [Fact]public void Empty_actual_project_is_a_valid_root(){using var t=new TempTree();Assert.Equal(t.Root,ProjectRootPolicy.Validate(t.Root));}
    [Fact]public void Capsule_rejects_internal_root(){using var t=new TempTree();var dir=t.Full(".arena-inbox");Directory.CreateDirectory(dir);Assert.Throws<InvalidOperationException>(()=>ConnectCapsule.Create(new(){ProjectFolder=dir,BridgeBin="adc-test-binding1",BridgeToken="test-only",BridgeEncrypted=true}));}
    [Fact]public void Capsule_requires_remote_only_work_and_no_empty_readme_fallback(){using var t=new TempTree();var p=ConnectCapsule.Create(new(){ProjectFolder=t.Root,BridgeBin="adc-test-binding1",BridgeToken="test-only",BridgeEncrypted=true}).Prompt;Assert.Contains(t.Root,p);Assert.Contains("Keine lokale Ersatzlösung",p);Assert.Contains("--stdin",p);Assert.Contains("fehlende README.md ist erlaubt",p);Assert.Contains("neuen Connect-Text",ConnectCapsule.RuntimeSource());Assert.DoesNotContain("read README.md",p);}
    [Theory][InlineData("AXON-CONNECT:old")][InlineData("AXON-ACK:old")]
    public async Task Stale_connect_cannot_return_a_successful_handshake(string note){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root,require:true);Queue(r,t.Root,"stale",note);await s.TaktAsync();await s.WaitForPublicationAsync();var result=BridgeProtocol.FromJson<BridgeResult>(BridgeCipher.Decrypt("adc-rc30test12345","result-stale.json",r.Files["result-stale.json"],"test-secret-only-1234"));Assert.Equal("fehler",result!.Status);Assert.False(s.PeerConfirmed);}
    [Fact]public async Task Required_handshake_blocks_write_without_mutating_project(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root,require:true);var job=new BridgeJob{Id="unbound",Kind="write",Projekt=t.Root,Path="blocked.txt",Content="must not appear"};r.Files["job-unbound.json"]=BridgeCipher.Encrypt("adc-rc30test12345","job-unbound.json",BridgeProtocol.ToJson(job),"test-secret-only-1234");await s.TaktAsync();await s.WaitForPublicationAsync();Assert.False(t.Exists("blocked.txt"));var result=BridgeProtocol.FromJson<BridgeResult>(BridgeCipher.Decrypt("adc-rc30test12345","result-unbound.json",r.Files["result-unbound.json"],"test-secret-only-1234"));Assert.Equal("AXON_HANDSHAKE_REQUIRED",result!.Hinweis);Assert.False(s.PeerConfirmed);}
    [Fact]public async Task Exact_ack_unlocks_project_actions_and_hello_pins_challenge(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root,require:true);Queue(r,t.Root,"ack","AXON-ACK:challenge-1");await s.TaktAsync();await s.WaitForPublicationAsync();var job=new BridgeJob{Id="bound",Kind="write",Projekt=t.Root,Path="allowed.txt",Content="remote"};r.Files["job-bound.json"]=BridgeCipher.Encrypt("adc-rc30test12345","job-bound.json",BridgeProtocol.ToJson(job),"test-secret-only-1234");await s.TaktAsync();await s.WaitForPublicationAsync();Assert.Equal("remote",t.Read("allowed.txt"));var hello=BridgeProtocol.FromJson<BridgeHello>(BridgeCipher.Decrypt("adc-rc30test12345","hello.json",r.Files["hello.json"],"test-secret-only-1234"));Assert.Equal("challenge-1",hello!.ConnectionId);}
    [Fact]public void Resume_rejects_another_challenge(){using var t=new TempTree();var s=Service(new Relay(),t.Root,require:true);Assert.False(s.RestoreConfirmedPeer("other",DateTimeOffset.UtcNow));Assert.False(s.PeerConfirmed);Assert.Null(s.ConfirmedPeerSnapshot());}
    [Fact]public void Restored_proof_keeps_real_old_contact_and_is_not_a_fresh_ping(){using var t=new TempTree();var s=Service(new Relay(),t.Root,require:true);var at=DateTimeOffset.UtcNow.AddDays(-1);Assert.True(s.RestoreConfirmedPeer("challenge-1",at));Assert.True(s.PeerRestored);Assert.Equal(at,s.PeerLastSeen);Assert.Equal("challenge-1",s.ConfirmedPeerSnapshot()!.Value.Id);s.ConnectionId="next";Assert.False(s.PeerConfirmed);Assert.False(s.PeerRestored);Assert.Null(s.ConfirmedPeerSnapshot());}
    [Fact]public async Task Real_next_job_refreshes_restored_contact_without_a_new_connect(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root,require:true);s.RestoreConfirmedPeer("challenge-1",DateTimeOffset.UtcNow.AddDays(-1));Queue(r,t.Root,"fresh");await s.TaktAsync();await s.WaitForPublicationAsync();Assert.False(s.PeerRestored);Assert.True(s.PeerLastSeen>DateTimeOffset.UtcNow.AddMinutes(-1));}
    [Fact] public async Task Poison_job_does_not_block_valid_job(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root);r.Files["job-aaa.json"]="invalid encrypted json";Queue(r,t.Root,"bbb");var results=await s.TaktAsync();await s.WaitForPublicationAsync();Assert.Contains(results,x=>x.Id=="bbb"&&x.Ok);Assert.False(r.Files.ContainsKey("job-aaa.json"));}
    [Fact] public async Task Expired_job_does_not_block_following_ping(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root);r.Files["job-aaa.json"]=BridgeCipher.Encrypt("adc-rc30test12345","job-aaa.json","{}","test-secret-only-1234",DateTimeOffset.UtcNow.AddHours(-1));Queue(r,t.Root,"bbb");Assert.Contains(await s.TaktAsync(),x=>x.Id=="bbb"&&x.Ok);await s.WaitForPublicationAsync();}
    [Fact] public async Task Wrong_project_is_rejected_without_hijacking(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root);Queue(r,t.Root+"-other","aaa");Queue(r,t.Root,"bbb");var results=await s.TaktAsync();await s.WaitForPublicationAsync();Assert.Single(results);Assert.Equal("bbb",results[0].Id);Assert.False(s.PeerConfirmed);}
    [Fact] public async Task Relay_or_regular_ping_is_not_connection_proof(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root);Queue(r,t.Root,"aaa");await s.TaktAsync();await s.WaitForPublicationAsync();Assert.NotNull(s.LetzterKontakt);Assert.False(s.PeerConfirmed);}
    [Fact] public async Task Correct_ack_confirms_but_wrong_challenge_does_not(){using var t=new TempTree();var r=new Relay();var s=Service(r,t.Root);Queue(r,t.Root,"aaa","AXON-ACK:wrong");await s.TaktAsync();Assert.False(s.PeerConfirmed);Queue(r,t.Root,"bbb","AXON-ACK:challenge-1");await s.TaktAsync();Assert.True(s.PeerConfirmed);Assert.NotNull(s.PeerLastSeen);await s.WaitForPublicationAsync();}
    [Fact] public async Task Manifest_http_failure_does_not_silence_next_ping(){using var t=new TempTree();var r=new Relay{FailManifest=true};var s=Service(r,t.Root);await s.TaktAsync();await s.WaitForPublicationAsync();Assert.NotNull(s.PublikationsFehler);Queue(r,t.Root,"aaa");Assert.Contains(await s.TaktAsync(),x=>x.Id=="aaa"&&x.Ok);await s.WaitForPublicationAsync();}
    [Fact] public async Task Slow_manifest_does_not_hold_control_channel(){using var t=new TempTree();var r=new Relay{StallManifest=true};var s=Service(r,t.Root);using var cancel=new CancellationTokenSource();var watch=System.Diagnostics.Stopwatch.StartNew();await s.TaktAsync(cancel.Token);Queue(r,t.Root,"aaa");Assert.Contains(await s.TaktAsync(cancel.Token),x=>x.Id=="aaa"&&x.Ok);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(3));cancel.Cancel();await s.WaitForPublicationAsync();}
    [Theory][InlineData("manifest")][InlineData("look")]
    public async Task Explicit_manifest_does_not_hold_later_ping(string kind)
    {
        using var t=new TempTree();var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var r=new Relay{ManifestGate=release.Task,ManifestStarted=started};var service=Service(r,t.Root);
        var job=new BridgeJob{Id="heavy",Kind=kind,Projekt=t.Root};r.Files["job-heavy.json"]=BridgeCipher.Encrypt("adc-rc30test12345","job-heavy.json",BridgeProtocol.ToJson(job),"test-secret-only-1234");
        try
        {
            await service.TaktAsync();await started.Task.WaitAsync(TimeSpan.FromSeconds(3));Queue(r,t.Root,"ping");
            var watch=System.Diagnostics.Stopwatch.StartNew();var result=await service.TaktAsync();Assert.Contains(result,x=>x.Id=="ping"&&x.Ok);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2));Assert.False(r.Files.ContainsKey("result-heavy.json"));
        }
        finally{release.TrySetResult();await service.WaitForPublicationAsync();}
        Assert.Contains(service.Verlauf,x=>x.Id=="heavy"&&x.Ok);
    }
    [Fact] public async Task Journal_snapshots_are_safe_during_parallel_publication()
    {
        var c=new BridgeChronik();var stand=new BridgeStand(1,1,"test","rc30",null,0,0,null);
        await Task.WhenAll(Task.Run(()=>{for(var i=0;i<1000;i++)c.Aufnehmen(["file"+i],[],[],DateTime.Now,_=>(1,"h"));}),Task.Run(()=>{for(var i=0;i<1000;i++){using var j=JsonDocument.Parse(c.ToJson(stand));_ = c.Eintraege.ToArray();}}));
        Assert.Equal(1000,c.Sequenz);Assert.Equal(BridgeChronik.MaxEintraege,c.Eintraege.Count);
    }
    [Fact] public async Task Two_services_cannot_cross_confirm(){using var a=new TempTree();using var b=new TempTree();var r1=new Relay();var r2=new Relay();var s1=Service(r1,a.Root);var s2=Service(r2,b.Root);Queue(r1,a.Root,"aaa","AXON-ACK:challenge-1");await Task.WhenAll(s1.TaktAsync(),s2.TaktAsync());await Task.WhenAll(s1.WaitForPublicationAsync(),s2.WaitForPublicationAsync());Assert.True(s1.PeerConfirmed);Assert.False(s2.PeerConfirmed);}
}

public class ConnectControllerTests
{
    private static Arbeitsbereich Tab(string root)=>new(){ProjectFolder=root,BridgeBin="adc-test-controller1",BridgeToken="test-secret-only",BridgeEncrypted=true};
    [Fact] public async Task Preparation_waits_for_persistence_before_returning_payload(){using var t=new TempTree();var c=new ConnectController();var saved=false;await c.PrepareAsync(Tab(t.Root),new Uri("https://arena.ai/agent"),new ArenaProviderAdapter(),_=>{saved=true;return Task.CompletedTask;});Assert.True(saved);Assert.Equal(ConnectPhase.AwaitingUserSend,c.Phase);}
    [Fact] public async Task Persistence_failure_is_not_success(){using var t=new TempTree();var c=new ConnectController();await Assert.ThrowsAsync<IOException>(()=>c.PrepareAsync(Tab(t.Root),new Uri("https://arena.ai/agent"),new ArenaProviderAdapter(),_=>throw new IOException("Speichern fehlgeschlagen")));Assert.Equal(ConnectPhase.Failed,c.Phase);}
    [Fact] public async Task Concurrent_connect_is_not_sent_twice(){using var t=new TempTree();var c=new ConnectController();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var first=c.PrepareAsync(Tab(t.Root),new Uri("https://arena.ai/agent"),new ArenaProviderAdapter(),_=>gate.Task);await Assert.ThrowsAsync<InvalidOperationException>(()=>c.PrepareAsync(Tab(t.Root),new Uri("https://arena.ai/agent"),new ArenaProviderAdapter(),_=>Task.CompletedTask));gate.SetResult();await first;}
    [Fact] public async Task Root_change_invalidates_prepared_handoff(){using var t=new TempTree();var c=new ConnectController();var tab=Tab(t.Root);await Assert.ThrowsAsync<InvalidOperationException>(()=>c.PrepareAsync(tab,new Uri("https://arena.ai/agent"),new ArenaProviderAdapter(),_=>{tab.ProjectFolder="other-root";return Task.CompletedTask;}));Assert.Equal(ConnectPhase.Failed,c.Phase);}
}

public class Rc30FeedbackTests
{
    [Fact]public void Runtime_contains_the_real_redirect_transport_not_an_unresolved_marker(){var source=ConnectCapsule.RuntimeSource();Assert.Contains("storage.filebin.net",source);Assert.Contains("redirect:'manual'",source);Assert.DoesNotContain("@AXON_HTTP@",source);Assert.DoesNotContain("redirect:'error'",source);}
    [Fact]public void Draft_payload_is_json_encoded_not_interpolated_as_code(){const string text="Entwurf '\" </script> \\ \n äöü";Assert.Contains(JsonSerializer.Serialize(text),DraftComposer.Script(text,new Uri("https://arena.ai/agent/chat/123")));}
    [Fact]public void Draft_script_guards_owner_location_and_existing_draft(){var script=DraftComposer.Script("test",new Uri("https://arena.ai/agent/123"));Assert.Contains("location.origin!==origin",script);Assert.Contains("location.pathname!==path",script);Assert.Contains("draft_not_empty",script);Assert.Contains("ambiguous_composer",script);}
    [Fact]public void Draft_script_never_submits_or_reads_chat_history(){var script=DraftComposer.Script("test",new Uri("https://arena.ai/agent"));Assert.DoesNotContain(".click(",script);Assert.DoesNotContain("requestSubmit",script);Assert.DoesNotContain("KeyboardEvent",script);Assert.DoesNotContain("fetch(",script);Assert.DoesNotContain("document.body.innerText",script);}
}
