namespace ComfyNetworkSense.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using ComfyQuestContracts;
using Xunit;

public sealed class RuntimeRunStatusTests
{
  [Fact]
  public async Task AtomicHeartbeatRetriesAcrossABoundedWindowsReader()
  {
    var root=Path.Combine(Path.GetTempPath(),"comfy-run-status-"+Guid.NewGuid().ToString("N"));
    try{
      var store=new RuntimeRunStatusStore(root);
      store.Write(Status("first"));
      var path=Path.Combine(root,"status","runs.json");
      var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
      var replacement=Task.Run(()=>store.Write(Status("second")));
      await Task.Delay(15);
      reader.Dispose();
      await replacement;
      Assert.Equal("second",store.Read().Runs[0].RunId);
      Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path),"*.tmp-*"));
    }finally{try{Directory.Delete(root,true);}catch{}}
  }

  [Fact]
  public async Task HeartbeatReaderRetriesAcrossABoundedAtomicReplacementWindow()
  {
    var root=Path.Combine(Path.GetTempPath(),"comfy-run-status-read-"+Guid.NewGuid().ToString("N"));
    try{
      var store=new RuntimeRunStatusStore(root);
      store.Write(Status("stable"));
      var path=Path.Combine(root,"status","runs.json");
      var writer=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
      var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var reading=Task.Run(()=>{started.SetResult(true);return store.Read();});
      await started.Task;
      await Task.Delay(15);
      writer.Dispose();
      Assert.Equal("stable",(await reading).Runs[0].RunId);
    }finally{try{Directory.Delete(root,true);}catch{}}
  }

  [Fact]
  public async Task DevChannelHeartbeatRetriesAcrossABoundedWindowsReader()
  {
    var root=Path.Combine(Path.GetTempPath(),"comfy-dev-status-"+Guid.NewGuid().ToString("N"));
    try{
      var store=new RuntimeDevChannelStatusStore(root);
      store.Write(DevStatus("first"));
      var path=Path.Combine(root,"status","dev-channel.json");
      var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
      var replacement=Task.Run(()=>store.Write(DevStatus("second")));
      await Task.Delay(15);
      reader.Dispose();
      await replacement;
      Assert.Equal("second",store.Read().State);
      Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path),"*.tmp-*"));
    }finally{try{Directory.Delete(root,true);}catch{}}
  }

  static RuntimeRunStatusDocument Status(string runId)=>new()
  {
    ObservedUtc=DateTimeOffset.UtcNow,
    Machine="OMEN",
    WorldUid="123",
    Runs=new[]{new RuntimeRunStatusEntry
    {
      RunId=runId,ScopeId="scope-"+runId,ExperienceId="guild-a",BindingZdo="10:20",
      ParticipantIds=new[]{"hero"},ContentHash="content",StageId="start",
    }},
  };

  static RuntimeDevChannelStatus DevStatus(string state)=>new()
  {
    SessionId="dev-test",ObservedUtc=DateTimeOffset.UtcNow,Armed=true,State=state,
  };
}
