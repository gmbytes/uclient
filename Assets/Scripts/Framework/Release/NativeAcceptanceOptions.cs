using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Framework.Release
{
    public sealed class NativeAcceptanceOptions
    {
        public bool Enabled { get; private set; }
        public string RunId { get; private set; }
        public string CaseId { get; private set; }
        public string PausePoint { get; private set; }
        public string StatePurpose { get; private set; }
        public bool LowSpace { get; private set; }
        public bool CancelOnTransfer { get; private set; }
        public bool Tok { get; private set; }
        public bool InvalidCredential { get; private set; }
        public bool AutoExit { get; private set; }
        public static NativeAcceptanceOptions Parse(bool allowed, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new NativeAcceptanceOptions();
            if (!allowed) throw new InvalidDataException("This native profile does not permit acceptance controls.");
            try
            {
                if (Encoding.UTF8.GetByteCount(json) > 4096) throw new InvalidDataException("Acceptance request is oversized.");
                var data = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                string[] fields = { "runId", "caseId", "pausePoint", "statePurpose", "lowSpace", "cancelOnTransfer", "tok", "invalidCredential", "autoExit" };
                if (data.Properties().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))) throw new InvalidDataException("Unknown acceptance control.");
                string run = (string)data["runId"], item = (string)data["caseId"], point = (string)data["pausePoint"], purpose = (string)data["statePurpose"];
                foreach (string id in new[] { run, item }) if (string.IsNullOrEmpty(id) || id.Length > 40 || !id.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) throw new InvalidDataException("Bounded run/case identity is required.");
                if (point != null && !new[] { "download.partial", "rename.before", "state.replace.before", "state.replace.after", "trial.persisted", "hotentry.executing" }.Contains(point)) throw new InvalidDataException("Unsupported acceptance pause boundary.");
                if (purpose != null && !new[] { "write", "candidate", "trial", "promote", "quarantine", "clearCandidate" }.Contains(purpose)) throw new InvalidDataException("Unsupported state-write purpose.");
                if (point != null && point.StartsWith("state.", StringComparison.Ordinal) && purpose == null) throw new InvalidDataException("A state pause requires the exact write purpose.");
                foreach (string flag in fields.Skip(4)) if (data[flag] != null && data[flag].Type != JTokenType.Boolean) throw new InvalidDataException("Acceptance flags must be booleans.");
                return new NativeAcceptanceOptions { Enabled = true, RunId = run, CaseId = item, PausePoint = point, StatePurpose = purpose,
                    LowSpace = (bool?)data["lowSpace"] ?? false, CancelOnTransfer = (bool?)data["cancelOnTransfer"] ?? false, Tok = (bool?)data["tok"] ?? false,
                    InvalidCredential = (bool?)data["invalidCredential"] ?? false, AutoExit = (bool?)data["autoExit"] ?? false };
            }
            catch (InvalidDataException) { throw; }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidCastException)
            { throw new InvalidDataException("Invalid acceptance controls.", error); }
        }
    }
}
