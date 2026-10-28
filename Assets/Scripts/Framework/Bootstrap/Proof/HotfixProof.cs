#if UNITY_EDITOR || MINIMAL_HOTFIX_PROOF
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.HotUpdate;
using HybridCLR;
using Google.Protobuf;
using Tok.Client.V1;
using UnityEngine;

namespace Client.Bootstrap.Proof
{
    public sealed class HotfixProof : MonoBehaviour
    {
        private async void Start()
        {
            var result = new ProofResult();
            string output = null;
            try
            {
                output = Argument("-hotfixResult");
                string root = Path.GetFullPath(Argument("-hotfixRoot"));
                string metadata = Path.GetFullPath(Argument("-metadataRoot"));
                string expected = Argument("-hotfixExpected");
                result.processId = System.Diagnostics.Process.GetCurrentProcess().Id;
                result.playerBuildId = File.ReadAllText(Path.Combine(metadata, "player-build-id.txt")).Trim();
                var files = Directory.GetFiles(metadata, "*.dll");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    var code = RuntimeApi.LoadMetadataForAOTAssembly(File.ReadAllBytes(file), HomologousImageMode.SuperSet);
                    Debug.Log("HOTFIX_METADATA " + Path.GetFileName(file) + " " + code);
                    if (code != LoadImageErrorCode.OK) throw new InvalidOperationException("Metadata load failed: " + Path.GetFileName(file) + " " + code);
                    result.metadataCount++;
                }
                if (result.metadataCount == 0) throw new InvalidOperationException("This Player's metadata archive is empty.");
                var original = new Frame { Version = 3, Request = new BusinessRequest { RequestId = ulong.MaxValue, MessageId = 101, Body = ByteString.CopyFrom(new byte[] { 8, 1 }) } };
                var decoded = Frame.Parser.ParseFrom(original.ToByteArray());
                result.envelopeVerified = decoded.Version == 3 && decoded.ContentCase == Frame.ContentOneofCase.Request && decoded.Request.RequestId == ulong.MaxValue && decoded.Request.Body.Equals(original.Request.Body);
                if (!result.envelopeVerified) throw new InvalidOperationException("The AOT envelope did not preserve uint64, bytes and oneof.");
                byte[] protocol = File.ReadAllBytes(Path.Combine(root, "Client.Game.Protocol.dll"));
                result.protocolDllSha256 = Hash(protocol);
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, "Client.Game.dll"));
                result.dllSha256 = Hash(bytes);
                var loader = new HotfixAssemblyLoader("Client.Game.Proof.Entry");
                var entry = loader.LoadOnce(new[] {
                    new VerifiedAssembly("Client.Game.Protocol", protocol, result.protocolDllSha256, new[] { "Client.Framework", "Google.Protobuf" }),
                    new VerifiedAssembly("Client.Game", bytes, result.dllSha256, new[] { "Client.Contracts", "Client.Game.Protocol" }) });
                result.loadOrder = new[] { "Client.Game.Protocol", "Client.Game" };
                result.addedTypePresent = entry.GetType().Assembly.GetType("Client.Game.Proof.AddedAnswer", false) != null;
                var host = FindObjectOfType<ClientBootstrap>();
                if (host == null) throw new InvalidOperationException("The AOT bootstrap host is missing.");
                var context = new ProofContext(host.Lifetime, result);
                await host.InitializeAsync(entry, context, CancellationToken.None);
                if (result.marker != expected || result.readyCount != 1) throw new InvalidOperationException("Unexpected hotfix execution result.");
                if (!result.emptyRegistryVerified) throw new InvalidOperationException("The hot entry did not execute the generated empty registry.");
                if (result.addedTypePresent != (expected == "proof-v2:42")) throw new InvalidOperationException("New-type presence does not match the selected DLL.");
                try { loader.LoadOnce(new[] { new VerifiedAssembly("Client.Game", bytes, result.dllSha256, Array.Empty<string>()) }); }
                catch (InvalidOperationException) { result.secondLoadRejected = true; }
                if (!result.secondLoadRejected) throw new InvalidOperationException("A second DLL load was accepted.");
                await host.ShutdownAsync(CancellationToken.None);
                result.passed = true;
            }
            catch (Exception exception) { result.error = exception.ToString(); Debug.LogException(exception); }
            if (output != null) File.WriteAllText(output, JsonUtility.ToJson(result, true) + "\n");
            Application.Quit(result.passed ? 0 : 1);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name + " is required.");
            return args[index + 1];
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private sealed class ProofContext : IClientContext, ILogService
        {
            private readonly ProofResult result;
            public ProofContext(CancellationToken lifetime, ProofResult result) { Lifetime = lifetime; this.result = result; }
            public IResourceService Resources => throw new NotSupportedException("Resources are not activated by the minimal DLL proof.");
            public IUIService UI => throw new NotSupportedException("The minimal DLL proof has no game UI service.");
            public ISessionClient Session => throw new NotSupportedException("The minimal DLL proof has no session service.");
            public ILogService Log => this;
            public CancellationToken Lifetime { get; }
            public void ReportReady() { result.readyCount++; }
            public void Write(ClientLogLevel level, string code, string message)
            {
                if (level == ClientLogLevel.Info && code == "PROTOCOL_PROOF" && message == "empty-registry")
                {
                    if (result.emptyRegistryVerified) throw new InvalidOperationException("The protocol proof executed twice.");
                    result.emptyRegistryVerified = true;
                    Debug.Log(code + " " + message);
                    return;
                }
                if (level != ClientLogLevel.Info || code != "HOTFIX_PROOF") throw new InvalidOperationException("Unexpected proof log.");
                if (result.marker != null) throw new InvalidOperationException("The hotfix executed more than one marker branch.");
                result.marker = message;
                Debug.Log(code + " " + message);
            }
        }

        [Serializable]
        private sealed class ProofResult
        {
            public bool passed;
            public int processId;
            public int metadataCount;
            public int readyCount;
            public bool addedTypePresent;
            public bool secondLoadRejected;
            public bool envelopeVerified;
            public bool emptyRegistryVerified;
            public string playerBuildId;
            public string dllSha256;
            public string protocolDllSha256;
            public string[] loadOrder;
            public string marker;
            public string error;
        }
    }
}
#endif
