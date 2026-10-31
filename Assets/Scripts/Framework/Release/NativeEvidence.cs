using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Framework.Release
{
    public interface IReleaseBoundaryObserver
    {
        void Preparation(VerifiedRelease release, string destination);
        void StateBoundary(string point, string purpose, string releaseId, ReleaseState state);
    }

    // Observes existing state transitions and synchronizes an external owned-process kill. It cannot write release state.
    public sealed class NativeEvidence : IFileFaultInjector, IReleaseBoundaryObserver, IDisposable
    {
        private readonly object sync = new object();
        private readonly NativeAcceptanceOptions options;
        private readonly int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        private readonly string directory;
        private FileStream events;
        private long sequence;
        private int paused;
        private string preparingRelease, preparingManifest;
        private readonly Dictionary<string,long> expectedDownloads=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        public string DirectoryPath => directory;
        public string AttemptId { get; }
        public NativeEvidence(string directory, NativeAcceptanceOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.directory = ReleaseFileSystem.Root(directory);
            if (!options.Enabled) return;
            AttemptId=Guid.NewGuid().ToString("N");RelativePathPolicy.RequireIdentifier(AttemptId);
            this.directory=ReleaseFileSystem.Child(this.directory,AttemptId);
            foreach(string name in new[]{"events.jsonl","checkpoint.json","continue","screen.png","complete.json"})ReleaseFileSystem.NoReparse(Path.Combine(this.directory,name));
            if(Directory.Exists(this.directory)||File.Exists(this.directory))throw new IOException("Native attempt evidence already exists.");
            Directory.CreateDirectory(this.directory);
            events = new FileStream(Path.Combine(this.directory,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
            Record("process.start",new { utc=DateTime.UtcNow });
        }
        public void Record(string kind, object data)
        {
            if (!options.Enabled) return;
            lock(sync)
            {
                if(events==null)return;
                var value = new JObject { ["runId"]=options.RunId,["caseId"]=options.CaseId,["attemptId"]=AttemptId,["processId"]=processId,["sequence"]=++sequence,["utc"]=DateTime.UtcNow,["kind"]=kind,["data"]=data==null?null:JToken.FromObject(data) };
                byte[] bytes=Encoding.UTF8.GetBytes(value.ToString(Formatting.None)+"\n");
                if(bytes.Length>256*1024 || events.Length+bytes.Length>32*1024*1024)throw new IOException("Native evidence budget exceeded.");
                events.Write(bytes,0,bytes.Length);events.Flush(true);
            }
        }
        public void Preparation(VerifiedRelease release,string destination)
        {
            preparingRelease=release.ReleaseId;preparingManifest=release.ManifestSha256;
            string root=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(destination)));
            string downloads=Path.Combine(root,"staging",ReleaseFileSystem.StageKey(release.ManifestSha256)+".partial","downloads");
            lock(sync){expectedDownloads.Clear();for(int i=0;i<release.Files.Count;i++)expectedDownloads[Path.GetFullPath(Path.Combine(downloads,i+".partial"))]=release.Files[i].Length;}
            Record("preparation",new {releaseId=preparingRelease,manifestSha256=preparingManifest,destination});
        }
        public long? ExpectedTransferLength(string path)
        {lock(sync)return expectedDownloads.TryGetValue(Path.GetFullPath(path),out long length)?length:(long?)null;}
        public bool IsIncompleteTransfer(string path,long bytes)
        {long? expected=ExpectedTransferLength(path);return expected.HasValue&&bytes>0&&bytes<expected.Value;}
        public void StateBoundary(string point,string purpose,string releaseId,ReleaseState state)
        { Checkpoint(point,purpose,new {purpose,releaseId,state}); }
        public void Hit(string point)
        { if(point=="rename.before")Checkpoint(point,null,new {releaseId=preparingRelease,manifestSha256=preparingManifest}); }
        public void Checkpoint(string point,string purpose,object data)
        {
            Record(point,data);
            if(!options.Enabled || options.PausePoint!=point || options.StatePurpose!=purpose || Interlocked.CompareExchange(ref paused,1,0)!=0)return;
            string token=options.RunId+":"+options.CaseId+":"+AttemptId+":"+processId+":"+point+":"+(purpose??"");
            byte[] bytes=Encoding.UTF8.GetBytes(new JObject { ["runId"]=options.RunId,["caseId"]=options.CaseId,["attemptId"]=AttemptId,["processId"]=processId,["point"]=point,["purpose"]=purpose,["token"]=token,["data"]=data==null?null:JToken.FromObject(data) }.ToString(Formatting.None));
            ReleaseFileSystem.WriteDurable(Path.Combine(directory,"checkpoint.json"),bytes);
            string resume=Path.Combine(directory,"continue");DateTime deadline=DateTime.UtcNow.AddSeconds(90);
            while(DateTime.UtcNow<deadline)
            {
                if(File.Exists(resume) && new FileInfo(resume).Length<=256 && File.ReadAllText(resume)==token){Record("checkpoint.continued",new {point,purpose});return;}
                Thread.Sleep(20);
            }
            throw new TimeoutException("Owned acceptance checkpoint expired: "+point);
        }
        public void Dispose(){lock(sync){events?.Dispose();events=null;}}
    }
}
