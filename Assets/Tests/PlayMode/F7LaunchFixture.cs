#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Release;
using GameFramework.Resource;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    internal sealed class F7LaunchFixture : IDisposable
    {
        internal readonly string Root;
        internal readonly ReleaseStateStore State;
        internal readonly PlayerIdentity Player;
        internal readonly ReleaseCoordinator Coordinator;
        internal readonly Faults Fault = new Faults();
        private readonly RSA key;
        internal F7LaunchFixture(IReleaseTransport updates=null)
        {
            Root=Path.GetFullPath(Path.Combine("Builds","F7PlayScratch",Guid.NewGuid().ToString("N")));
            key=new RSACryptoServiceProvider(2048){PersistKeyInCsp=false};
            var verifier=(ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier),BindingFlags.Instance|BindingFlags.NonPublic,null,
                new object[]{new Dictionary<string,RSAParameters>{["f7-play"]=key.ExportParameters(false)}},null);
            Player=new PlayerIdentity("p","Windows","x64","2022.3.25f1","1",new string('a',64),"a");
            State=new ReleaseStateStore(Root,verifier,Player,Fault);
            var serializer=new PackageVersionListSerializer();serializer.RegisterSerializeCallback(2,BuiltinVersionListSerializer.PackageVersionListSerializeCallback_V2);
            byte[] native;
            using(var stream=new MemoryStream()){if(!serializer.Serialize(stream,new PackageVersionList("0.1.0",1,new PackageVersionList.Asset[0],new PackageVersionList.Resource[0],new PackageVersionList.FileSystem[0],new PackageVersionList.ResourceGroup[0]),2))throw new InvalidDataException("Package fixture serialization failed.");native=stream.ToArray();}
            byte[] dll={1,2,3};
            var transport=new Transport(new Dictionary<string,byte[]>{{"Resources/GameFrameworkVersion.dat",native},{"Hotfix/Game.dll",dll}});
            var store=new ReleaseStore(Root,new ReleaseDownloader(transport),State,new DiskBudget(_=>long.MaxValue));
            foreach(string id in new[]{"K","C"})
            {
                string manifest="{\"schemaVersion\":1,\"keyId\":\"f7-play\",\"releaseId\":\""+id+"\",\"player\":{\"playerBuildId\":\"p\",\"platform\":\"Windows\",\"architecture\":\"x64\",\"unityVersion\":\"2022.3.25f1\",\"contractVersion\":\"1\",\"envelopeSha256\":\""+new string('a',64)+"\",\"aotMetadataSetId\":\"a\"},\"protocolSha256\":\""+new string('b',64)+"\",\"resourceVersionListPath\":\"Resources/GameFrameworkVersion.dat\",\"files\":["+FileEntry("Resources/GameFrameworkVersion.dat",native,"NativeResource")+","+FileEntry("Hotfix/Game.dll",dll,"HotfixDll")+"],\"assemblies\":[{\"name\":\"Game\",\"path\":\"Hotfix/Game.dll\",\"dependencies\":[\"Client.Contracts\"]}],\"assemblyOrder\":[\"Game\"]}";
                byte[] raw=Encoding.UTF8.GetBytes(manifest);var verified=verifier.Verify(raw,key.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),Player);
                store.PrepareAsync(verified,null,CancellationToken.None).GetAwaiter().GetResult();
            }
            State.WriteAtomic(new ReleaseState{KnownGood="K",Candidate="C"});
            Coordinator=new ReleaseCoordinator(State,new NativeLaunchConfiguration(Player,"Client.Game.Entry","Page",TimeSpan.FromSeconds(10)),updates==null?null:store,updates==null?null:verifier,updates);
        }
        private static string FileEntry(string path,byte[] bytes,string kind)
        {using(var hash=SHA256.Create())return "{\"path\":\""+path+"\",\"length\":"+bytes.Length+",\"sha256\":\""+BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()+"\",\"kind\":\""+kind+"\"}";}
        public void Dispose(){Coordinator.Dispose();key.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
        internal sealed class Faults:IFileFaultInjector
        {internal int Writes;internal string Checkpoint;internal Action<string> At;public void Hit(string point){if(point=="state.write.before")Writes++;At?.Invoke(point);if(point==Checkpoint)throw new IOException("Injected "+point);}}
        private sealed class Transport:IReleaseTransport
        {private readonly Dictionary<string,byte[]> data;internal Transport(Dictionary<string,byte[]> data){this.data=data;}public Task DownloadAsync(string path,string destination,CancellationToken token){token.ThrowIfCancellationRequested();File.WriteAllBytes(destination,data[path]);return Task.CompletedTask;}}
    }
}
#endif
