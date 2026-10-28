using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Framework.Release
{
    public sealed class ReleaseState
    {
        public string KnownGood { get; set; }
        public string PreviousGood { get; set; }
        public string Candidate { get; set; }
        public int Attempt { get; set; }
        public string[] BadReleaseIds { get; set; } = Array.Empty<string>();
    }
    public sealed class ReleaseStateStore
    {
        // Applies to the complete schema-1 UTF-8 document on both read and write; never truncate IDs.
        public const int MaximumDocumentBytes = 256 * 1024;
        private readonly ManifestVerifier verifier;
        private readonly PlayerIdentity player;
        private readonly IFileFaultInjector faults;
        private readonly object sync;
        public string Root { get; }
        public IReadOnlyList<Exception> RecoveryDiagnostics { get; private set; } = Array.Empty<Exception>();
        public ReleaseStateStore(string root, ManifestVerifier verifier, PlayerIdentity player, IFileFaultInjector faults = null)
        {
            Root = ReleaseFileSystem.Root(root);
            this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.faults = faults ?? new NoFileFaults(); sync = ReleaseFileSystem.StateLock(Root);
        }
        public ReleaseState Read()
        {
            lock (sync)
            {
                var errors = new List<Exception>();
                foreach (string name in new[] { "state.json", "state.json.bak" })
                {
                    try
                    {
                        string path = ReleaseFileSystem.Child(Root, name);
                        if (Directory.Exists(path)) throw new InvalidDataException("State source is a directory.");
                        if (!File.Exists(path)) continue;
                        if (new FileInfo(path).Length > MaximumDocumentBytes) throw new InvalidDataException("State document is too large.");
                        byte[] bytes = File.ReadAllBytes(path);
                        if (bytes.Length > MaximumDocumentBytes) throw new InvalidDataException("State document is too large.");
                        ReleaseState state = Parse(new UTF8Encoding(false, true).GetString(bytes));
                        Validate(state);
                        RecoveryDiagnostics = Array.AsReadOnly(errors.ToArray());
                        return state;
                    }
                    catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is JsonException || error is ReleaseVerificationException || error is ArgumentException)
                    { errors.Add(new InvalidDataException("Invalid release state source: " + name, error)); }
                }
                RecoveryDiagnostics = Array.AsReadOnly(errors.ToArray());
                if (errors.Count > 0) throw new AggregateException("No valid primary or backup release state is available.", errors);
                return new ReleaseState();
            }
        }
        public void WriteAtomic(ReleaseState state)
        {
            lock (sync)
            {
                Validate(state);
                var json = new JObject
                {
                    ["schemaVersion"] = 1, ["knownGood"] = state.KnownGood, ["previousGood"] = state.PreviousGood,
                    ["candidate"] = state.Candidate, ["attempt"] = state.Attempt, ["badReleaseIds"] = new JArray(state.BadReleaseIds)
                };
                byte[] bytes = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
                if (bytes.Length > MaximumDocumentBytes) throw new InvalidDataException("State document is too large.");
                Directory.CreateDirectory(Root);
                string primary = ReleaseFileSystem.Child(Root, "state.json");
                string backup = ReleaseFileSystem.Child(Root, "state.json.bak");
                string temp = ReleaseFileSystem.Child(Root, "state." + Guid.NewGuid().ToString("N") + ".partial");
                faults.Hit("state.write.before");
                ReleaseFileSystem.WriteDurable(temp, bytes);
                faults.Hit("state.write.after");
                faults.Hit("state.replace.before");
                if (File.Exists(primary)) File.Replace(temp, primary, backup);
                else File.Move(temp, primary);
                // Test-only crash boundary; production's no-op cannot throw after successful commit.
                faults.Hit("state.replace.after");
            }
        }
        internal void PublishCandidate(string releaseId, CancellationToken token)
        {
            lock (sync)
            {
                ReleaseState latest = Read();
                if (latest.Attempt > 0) throw new InvalidOperationException("An unfinished candidate trial must be recovered first.");
                if (latest.BadReleaseIds.Contains(releaseId, StringComparer.Ordinal)) throw new InvalidOperationException("A quarantined release cannot be prepared.");
                latest.Candidate = releaseId;
                token.ThrowIfCancellationRequested();
                WriteAtomic(latest);
            }
        }
        internal VerifiedRelease VerifySlot(string releaseId)
        {
            ReleaseFileSystem.ValidateId(releaseId);
            return VerifyDirectory(ReleaseFileSystem.Child(Root, "slots/" + releaseId), releaseId);
        }
        internal VerifiedRelease VerifyDirectory(string directory, string releaseId)
        {
            string manifest = ReleaseFileSystem.Child(directory, "release.manifest.json");
            string signature = ReleaseFileSystem.Child(directory, "release.manifest.sig");
            if (new FileInfo(manifest).Length > ManifestVerifier.MaximumDocumentBytes || new FileInfo(signature).Length != 256)
                throw new InvalidDataException("Invalid stored release evidence length.");
            VerifiedRelease release = verifier.Verify(File.ReadAllBytes(manifest), File.ReadAllBytes(signature), player);
            if (release.ReleaseId != releaseId) throw new InvalidDataException("Stored release ID does not match slot.");
            ReleaseFileSystem.ValidateInventory(release);
            foreach (ReleaseFile file in release.Files)
                ReleaseFileSystem.VerifyFile(ReleaseFileSystem.Child(directory, file.Path), file, CancellationToken.None);
            return release;
        }
        private void Validate(ReleaseState state)
        {
            if (state == null || state.Attempt < 0 || state.Attempt > 0 && state.Candidate == null ||
                state.BadReleaseIds == null || state.BadReleaseIds.Length > 4096 ||
                state.BadReleaseIds.Distinct(StringComparer.Ordinal).Count() != state.BadReleaseIds.Length)
                throw new InvalidDataException("Invalid release state structure.");
            foreach (string id in state.BadReleaseIds) ReleaseFileSystem.ValidateId(id);
            foreach (string id in new[] { state.KnownGood, state.PreviousGood, state.Candidate }.Where(id => id != null).Distinct(StringComparer.Ordinal))
            {
                ReleaseFileSystem.ValidateId(id);
                if (state.BadReleaseIds.Contains(id, StringComparer.Ordinal)) throw new InvalidDataException("Active release references cannot be quarantined.");
                VerifySlot(id);
            }
        }
        private static ReleaseState Parse(string text)
        {
            JObject json;
            using (var input = new StringReader(text))
            using (var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 })
            {
                json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException("Trailing release state content.");
            }
            string[] fields = { "schemaVersion", "knownGood", "previousGood", "candidate", "attempt", "badReleaseIds" };
            if (json.Properties().Count() != fields.Length || fields.Any(name => json.Property(name, StringComparison.Ordinal) == null) ||
                json["schemaVersion"].Type != JTokenType.Integer || json["schemaVersion"].ToString() != "1" || json["attempt"].Type != JTokenType.Integer ||
                !(json["badReleaseIds"] is JArray bad) || bad.Any(id => id.Type != JTokenType.String)) throw new InvalidDataException("Invalid release state schema.");
            int attempt;
            if (!int.TryParse(json["attempt"].ToString(), out attempt)) throw new InvalidDataException("Invalid release attempt.");
            return new ReleaseState { KnownGood = Reference(json["knownGood"]), PreviousGood = Reference(json["previousGood"]),
                Candidate = Reference(json["candidate"]), Attempt = attempt, BadReleaseIds = bad.Values<string>().ToArray() };
        }
        private static string Reference(JToken value)
        {
            if (value.Type == JTokenType.Null) return null;
            if (value.Type != JTokenType.String) throw new InvalidDataException("Invalid release reference.");
            return (string)value;
        }
    }
}
