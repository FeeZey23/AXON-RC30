using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ArenaDesktopCompanion.Core.Product;
using ArenaDesktopCompanion.Core.Settings;
namespace ArenaDesktopCompanion.Core.Product;

public sealed record ToolResult(int ExitCode,string Output,string Error);
public static class ProductTools
{
    public static string GitPolicyFile => Path.Combine(AppContext.BaseDirectory,"Product","Empty.gitconfig");
    public static string Gh => File.Exists(Path.Combine(AppContext.BaseDirectory,"Tools","GitHub","gh.exe")) ? Path.Combine(AppContext.BaseDirectory,"Tools","GitHub","gh.exe") : "gh";
    public static async Task<ToolResult> RunAsync(string exe,IEnumerable<string> args,string? cwd=null,string? input=null,Action<string>? progress=null,TimeSpan? timeout=null,CancellationToken token=default)
    {
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(token);cancel.CancelAfter(timeout??TimeSpan.FromMinutes(2));
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,WorkingDirectory=cwd??AppContext.BaseDirectory};
        foreach(var a in args)start.ArgumentList.Add(a);
        start.Environment["GH_HOST"]="github.com";start.Environment["GIT_TERMINAL_PROMPT"]="0";start.Environment["GH_PROMPT_DISABLED"]="1";start.Environment["GIT_TRACE_REDACT"]="1";
        foreach(var k in new[]{"GIT_TRACE","GIT_TRACE_CURL","GIT_CURL_VERBOSE","GH_DEBUG"})start.Environment.Remove(k);
        if(exe=="git"){start.Environment["GIT_CONFIG_NOSYSTEM"]="1";start.Environment["GIT_CONFIG_GLOBAL"]=GitPolicyFile;start.Environment["GIT_ALLOW_PROTOCOL"]="https";}
        using var p=new Process{StartInfo=start};if(!p.Start())throw new IOException("Tool did not start.");
        async Task<string> Drain(StreamReader reader){var buffer=new System.Text.StringBuilder();while(await reader.ReadLineAsync(cancel.Token) is{} line){if(buffer.Length<2_000_000)buffer.AppendLine(line);progress?.Invoke(line);}return buffer.ToString();}
        var stdout=Drain(p.StandardOutput);var stderr=Drain(p.StandardError);
        try{if(input is not null)await p.StandardInput.WriteAsync(input);p.StandardInput.Close();await p.WaitForExitAsync(cancel.Token);return new(p.ExitCode,await stdout,await stderr);}
        catch{try{if(!p.HasExited)p.Kill(true);}catch(InvalidOperationException){}try{await Task.WhenAll(stdout,stderr);}catch(OperationCanceledException){}throw;}
    }
    public static async Task<string> GhAsync(IEnumerable<string> args,string? cwd=null,string? input=null,Action<string>? progress=null,TimeSpan? timeout=null,CancellationToken token=default)
    {
        var r=await RunAsync(Gh,args,cwd,input,progress,timeout,token);if(r.ExitCode!=0)throw new InvalidOperationException("GitHub CLI: "+ArenaDesktopCompanion.Core.Agents.SecretRedaction.Clean(r.Error.Length>0?r.Error:r.Output));return r.Output;
    }
    public static async Task<JsonDocument> ApiAsync(string path,string? json=null,CancellationToken token=default)
    {
        var args=new List<string>{"api",path,"--hostname","github.com"};if(json is not null)args.AddRange(new[]{"--method","POST","--input","-"});
        return JsonDocument.Parse(await GhAsync(args,input:json,token:token));
    }
    public static Task GitAsync(string directory,params string[] args) => GitAsync(directory,CancellationToken.None,args);
    public static async Task GitAsync(string directory,CancellationToken token,params string[] args)
    {
        if(!File.Exists(GitPolicyFile))throw new FileNotFoundException("Complete AXON Git policy missing.");
        var helper="!\""+Gh.Replace('\\','/')+"\" auth git-credential";
        var r=await RunAsync("git",new[]{"-c","core.hooksPath="+GitPolicyFile,"-c","credential.helper=","-c","credential.https://github.com.helper="+helper}.Concat(args),directory,timeout:TimeSpan.FromMinutes(10),token:token);
        if(r.ExitCode!=0)throw new InvalidOperationException("Git: "+r.Error);
    }
}

public sealed record UpdateOffer(string Tag,string Version,string AssetName,string Sha256,string Digest,string Notes);
public sealed class GitHubService
{
    public async Task<string> AccountAsync(CancellationToken token=default) { using var user=await ProductTools.ApiAsync("user",token:token);return user.RootElement.GetProperty("login").GetString()!; }
    public async Task LoginAsync(Action<string> progress,CancellationToken token=default) => await ProductTools.GhAsync(new[]{"auth","login","--hostname","github.com","--git-protocol","https","--web","--clipboard","--scopes","repo,workflow"},progress:progress,timeout:TimeSpan.FromMinutes(16),token:token);
    public async Task<string> CloneAsync(string url,CancellationToken token=default)
    {
        var repository=ProductPolicy.ParseRepository(url);token.ThrowIfCancellationRequested();
        var folder=AutomaticProjects.Create(AppContext.BaseDirectory);
        // Empty allocated folders are kept on failure, never recursively delete user projects.
        await ProductTools.GitAsync(folder,token,"clone","--","https://github.com/"+repository+".git",folder);return folder;
    }
    public async Task<UpdateOffer?> CheckUpdateAsync(CancellationToken token=default)
    {
        using var releases=await ProductTools.ApiAsync("repos/"+ProductPolicy.Repository+"/releases?per_page=30",token:token);
        foreach(var release in releases.RootElement.EnumerateArray())
        {
            if(release.GetProperty("draft").GetBoolean())continue;
            var assets=release.GetProperty("assets").EnumerateArray().ToArray();var manifest=assets.FirstOrDefault(a=>a.GetProperty("name").GetString()=="axon-update.json");if(manifest.ValueKind==JsonValueKind.Undefined)continue;
            var id=manifest.GetProperty("id").GetInt64();
            var manifestText=await ProductTools.GhAsync(new[]{"api","repos/"+ProductPolicy.Repository+"/releases/assets/"+id,"-H","Accept: application/octet-stream"},token:token);
            using var doc=JsonDocument.Parse(manifestText);var m=doc.RootElement;
            if(m.GetProperty("product").GetString()!="AXON.RC30")throw new InvalidDataException("Wrong update product.");
            var version=m.GetProperty("version").GetString()!;if(!Version.TryParse(version,out var remote)||remote<=new Version(ProductPolicy.CurrentVersion.Major,ProductPolicy.CurrentVersion.Minor,ProductPolicy.CurrentVersion.Build))continue;
            var name=m.GetProperty("file").GetString()!;if(Path.GetFileName(name)!=name||!name.EndsWith(".msi",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe update asset name.");
            var asset=assets.Single(a=>a.GetProperty("name").GetString()==name);var digest=asset.GetProperty("digest").GetString()!;
            _=UpdateManifest.Parse(manifestText,digest);
            var bundle=m.GetProperty("bootstrapper");name=bundle.GetProperty("file").GetString()!;var hash=bundle.GetProperty("sha256").GetString()!.ToLowerInvariant();
            var remoteName=UpdateManifest.GitHubBootstrapperAsset(name);digest=assets.Single(a=>a.GetProperty("name").GetString()==remoteName).GetProperty("digest").GetString()!;
            UpdateManifest.ValidateBootstrapper(name,hash,digest);
            return new(release.GetProperty("tag_name").GetString()!,version,remoteName,hash,digest,release.GetProperty("body").GetString()??"");
        }
        return null;
    }
    public async Task<string> DownloadUpdateAsync(UpdateOffer offer,CancellationToken token=default)
    {
        if(offer.AssetName!=UpdateManifest.GitHubBootstrapperAsset("AXON RC30 Installer.exe"))throw new InvalidDataException("Unexpected GitHub update asset.");
        var staging=Path.Combine(Rc30Paths.DataRoot,"Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        await ProductTools.GhAsync(new[]{"release","download",offer.Tag,"--repo",ProductPolicy.Repository,"--pattern",offer.AssetName,"--dir",staging},timeout:TimeSpan.FromMinutes(10),token:token);
        var path=Path.Combine(staging,offer.AssetName);using var s=File.OpenRead(path);var hash=Convert.ToHexString(await SHA256.HashDataAsync(s,token)).ToLowerInvariant();if(hash!=offer.Sha256)throw new InvalidDataException("Downloaded update hash mismatch; installation refused.");s.Dispose();var local=Path.Combine(staging,"AXON RC30 Installer.exe");File.Move(path,local);return local;
    }
}

public interface IGitHubPublisher { Task<string> PublishAsync(string project,string name,string description,bool isPrivate,IReadOnlyList<string> selected,CancellationToken token=default); }
public sealed class GitHubPublisher : IGitHubPublisher
{
    public static IReadOnlyList<string> ReviewFiles(string root)
    {
        var result=new List<string>();var pending=new Stack<string>();pending.Push(root);long total=0;
        while(pending.TryPop(out var dir))foreach(var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if(File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))continue;
            var relative=Path.GetRelativePath(root,entry);if(!PublicationPolicy.IsAllowed(relative))continue;
            if(Directory.Exists(entry)){pending.Push(entry);continue;}
            var info=new FileInfo(entry);total+=info.Length;if(info.Length>20_000_000||total>100_000_000||result.Count>=3000)throw new IOException("Upload review limit: 3000 files / 20 MB per file / 100 MB total.");
            if(PublicationPolicy.ContainsSecret(File.ReadAllText(entry)))throw new InvalidDataException("Possible credential in "+relative+". Remove it before publishing.");result.Add(relative);
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<string> PublishAsync(string project,string name,string description,bool isPrivate,IReadOnlyList<string> selected,CancellationToken token=default)
    {
        ProductPolicy.ValidateRepositoryName(name);var reviewed=(await Task.Run(()=>ReviewFiles(project),token)).ToHashSet(StringComparer.OrdinalIgnoreCase);if(selected.Count==0||selected.Any(f=>!reviewed.Contains(f)))throw new InvalidOperationException("Select reviewed project files first.");
        using var user=await ProductTools.ApiAsync("user",token:token);var login=user.RootElement.GetProperty("login").GetString()!;var id=user.RootElement.GetProperty("id").GetInt64();
        var stage=Path.Combine(Rc30Paths.DataRoot,"Publish",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
        foreach(var relative in selected){token.ThrowIfCancellationRequested();var target=Path.Combine(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);var source=Path.GetFullPath(Path.Combine(project,relative));
            var walk=Path.GetFullPath(project);foreach(var part in relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)){walk=Path.Combine(walk,part);if(File.GetAttributes(walk).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Project link changed during review; review again.");}
            await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);}
        // Scan the copied snapshot as well; no git metadata or user remotes are changed.
        _=await Task.Run(()=>ReviewFiles(stage),token);
        var scanner=Path.Combine(AppContext.BaseDirectory,"Tools","Gitleaks","gitleaks.exe");
        if(!File.Exists(scanner))throw new FileNotFoundException("Maintained publication scanner missing. Install the complete AXON package before publishing.");
        var scan=await ProductTools.RunAsync(scanner,new[]{"dir",stage,"--config",Path.Combine(AppContext.BaseDirectory,"Product","Publication.gitleaks.toml"),"--gitleaks-ignore-path",Path.Combine(AppContext.BaseDirectory,"Product","Publication.gitleaksignore"),"--ignore-gitleaks-allow","--no-banner","--redact","--report-format","json","--report-path",Path.Combine(Rc30Paths.DataRoot,"Publish","last-secret-report.json")},stage,timeout:TimeSpan.FromMinutes(3),token:token);
        PublicationPolicy.EnsureScannerPassed(scan.ExitCode);
        await ProductTools.GitAsync(stage,token,"init","-b","main");await ProductTools.GitAsync(stage,token,"config","--local","user.name",login);await ProductTools.GitAsync(stage,token,"config","--local","user.email",id+"+"+login+"@users.noreply.github.com");
        await ProductTools.GitAsync(stage,token,"add","--all");await ProductTools.GitAsync(stage,token,"-c","commit.gpgsign=false","commit","-m","Initial project upload from AXON RC30");
        var full=login+"/"+name;
        await ProductTools.GhAsync(new[]{"repo","create",full,isPrivate?"--private":"--public","--description",description},token:token);
        using(var check=await ProductTools.ApiAsync("repos/"+full,token:token))if(check.RootElement.GetProperty("private").GetBoolean()!=isPrivate)throw new InvalidOperationException("Repository visibility mismatch; nothing pushed.");
        await ProductTools.GitAsync(stage,token,"remote","add","origin","https://github.com/"+full+".git");await ProductTools.GitAsync(stage,token,"push","-u","origin","main");return "https://github.com/"+full;
        }finally{if(Directory.Exists(stage)){foreach(var f in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories))File.SetAttributes(f,FileAttributes.Normal);Directory.Delete(stage,true);}}
    }
}
