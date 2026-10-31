using ArenaDesktopCompanion.Core.Settings;
namespace AXON.Connector;

public enum ConnectPhase { Idle, Preparing, AwaitingUserSend, Connected, Failed }
public sealed class ConnectController
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public ConnectPhase Phase { get; private set; }
    public string? Error { get; private set; }
    public async Task<PreparedConnect> PrepareAsync(Arbeitsbereich tab,Uri chat,IChatProviderAdapter provider,Func<PreparedConnect,Task> persistAndActivate,CancellationToken token=default)
    {
        if(!await _gate.WaitAsync(0,token).ConfigureAwait(false))throw new InvalidOperationException("Connect läuft bereits.");
        try
        {
            Phase=ConnectPhase.Preparing;Error=null;
            if(!provider.IsSupportedChat(chat))throw new InvalidOperationException("Kein unterstützter Agent-Chat.");
            var id=tab.Id;var root=tab.ProjectFolder;var bin=tab.BridgeBin;
            var prepared=ConnectCapsule.Create(tab);
            await persistAndActivate(prepared).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(tab.Id!=id||tab.ProjectFolder!=root||tab.BridgeBin!=bin)throw new InvalidOperationException("Sitzung/Projekt während Connect geändert; Übergabe verworfen.");
            // The supplied Arena adapter deliberately has no unapproved automatic sender.
            Phase=ConnectPhase.AwaitingUserSend;return prepared;
        }
        catch(Exception ex){Error=ex.Message;Phase=ConnectPhase.Failed;throw;}
        finally{_gate.Release();}
    }
    public void Observe(ConnectionLight light)
    {
        if(light==ConnectionLight.Green){Phase=ConnectPhase.Connected;Error=null;}
        else if(Phase==ConnectPhase.Connected)Phase=ConnectPhase.Idle;
    }
}
