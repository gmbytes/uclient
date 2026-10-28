using System;
using System.IO;
using System.Reflection;
using Client.Framework.HotUpdate;
using HybridCLR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Client.Editor.Build
{
    // The diagnostic value is process-local. persistentValue is never read or written.
    public sealed class ReleaseLinkerSymbolPolicy : IDisposable
    {
        private readonly object diagnostic, previous;
        private readonly PropertyInfo value;
        private bool disposed;
        private static ReleaseLinkerSymbolPolicy active;
        private readonly string arguments, fingerprint;
        public static string Arguments => active?.arguments ?? ReadArguments();
        public static string Fingerprint => active?.fingerprint;
        private ReleaseLinkerSymbolPolicy(string manifest)
        {
            if (active != null) throw new InvalidOperationException("A linker policy scope is already active.");
            if (new FileInfo(manifest).Length > 64 * 1024) throw new InvalidDataException("Linker policy evidence is too large.");
            var policy = JObject.Parse(File.ReadAllText(manifest), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if ((int?)policy["schemaVersion"] != 1 || (int?)policy["compilerExitCode"] != 0) throw new InvalidDataException("Invalid linker policy evidence.");
            string helper = (string)policy["helperPath"], source = (string)policy["sourcePath"];
            if (HotfixLoadPlan.Hash(File.ReadAllBytes(helper)) != (string)policy["helperSha256"] || HotfixLoadPlan.Hash(File.ReadAllBytes(source)) != (string)policy["sourceSha256"])
                throw new InvalidDataException("The linker helper/source differs from its build evidence.");
            string deploy = Path.Combine(SettingsUtil.LocalIl2CppDir, "build/deploy");
            if (!Directory.Exists(deploy)) deploy = Path.Combine(EditorApplication.applicationContentsPath, "il2cpp/build/deploy");
            if (HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(deploy, "monolinker.dll"))) != (string)policy["monoLinkerSha256"] ||
                HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(deploy, "UnityLinker.dll"))) != (string)policy["unityLinkerSha256"])
                throw new InvalidDataException("The linker helper API/toolchain differs from its original pinned compilation.");
            diagnostic = GetDiagnostic(); value = diagnostic.GetType().GetProperty("value"); previous = value.GetValue(diagnostic);
            string prior = previous as string ?? "";
            if (helper.IndexOf('"') >= 0) throw new InvalidDataException("Invalid linker helper path.");
            string custom = "--custom-step=-UnityOutputStep:Client.Build.Linker.DisableSymbolsStep," + helper;
            arguments = string.IsNullOrEmpty(prior) ? custom : prior + " " + custom;
            fingerprint = JsonConvert.SerializeObject(new { sourceSha256 = (string)policy["sourceSha256"], helperSha256 = (string)policy["helperSha256"],
                monoLinkerSha256 = (string)policy["monoLinkerSha256"], unityLinkerSha256 = (string)policy["unityLinkerSha256"], priorArguments = prior,
                customStep = "-UnityOutputStep:Client.Build.Linker.DisableSymbolsStep,<verified-helper>", linkSymbols = false });
            try
            {
                value.SetValue(diagnostic, arguments);
                if ((string)value.GetValue(diagnostic) != arguments) throw new InvalidOperationException("The process-local linker policy was not applied.");
            }
            catch { value.SetValue(diagnostic, previous); throw; }
            active = this;
        }
        public static ReleaseLinkerSymbolPolicy Apply(string manifest) => new ReleaseLinkerSymbolPolicy(manifest);
        private static object GetDiagnostic() => typeof(Debug).GetMethod("GetDiagnosticSwitch", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "VMUnityLinkerAdditionalArgs" });
        private static string ReadArguments() { object diagnostic = GetDiagnostic(); return diagnostic.GetType().GetProperty("value").GetValue(diagnostic) as string ?? ""; }
        public void Dispose()
        {
            if (disposed) return;
            try { value.SetValue(diagnostic, previous); }
            finally { active = null; disposed = true; }
        }
    }
}
