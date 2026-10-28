using System;
using System.Collections.Generic;
using System.Linq;
using HybridCLR.Settings;
using UnityEditor;
using UnityEngine;

namespace Client.Editor.Build
{
    // Captures live settings as well as their exact serialized files. Extend this list with any new setting a pipeline changes.
    public sealed class BuildSourceTransaction : IDisposable
    {
        private readonly SourceFileTransaction files;
        private readonly List<Action> restore = new List<Action>();
        private bool disposed;
        private BuildSourceTransaction(IEnumerable<string> sourcePaths)
        {
            files = SourceFileTransaction.Capture(sourcePaths.Concat(new[] { "ProjectSettings/ProjectSettings.asset", "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/HybridCLRSettings.asset", "ProjectSettings/EditorSettings.asset" }));
            Capture(() => PlayerSettings.companyName, v => PlayerSettings.companyName = v);
            Capture(() => PlayerSettings.productName, v => PlayerSettings.productName = v);
            Capture(() => PlayerSettings.bundleVersion, v => PlayerSettings.bundleVersion = v);
            Capture(() => PlayerSettings.defaultScreenWidth, v => PlayerSettings.defaultScreenWidth = v);
            Capture(() => PlayerSettings.defaultScreenHeight, v => PlayerSettings.defaultScreenHeight = v);
            Capture(() => PlayerSettings.resizableWindow, v => PlayerSettings.resizableWindow = v);
            Capture(() => PlayerSettings.defaultInterfaceOrientation, v => PlayerSettings.defaultInterfaceOrientation = v);
            Capture(() => PlayerSettings.allowedAutorotateToPortrait, v => PlayerSettings.allowedAutorotateToPortrait = v);
            Capture(() => PlayerSettings.allowedAutorotateToPortraitUpsideDown, v => PlayerSettings.allowedAutorotateToPortraitUpsideDown = v);
            Capture(() => PlayerSettings.allowedAutorotateToLandscapeLeft, v => PlayerSettings.allowedAutorotateToLandscapeLeft = v);
            Capture(() => PlayerSettings.allowedAutorotateToLandscapeRight, v => PlayerSettings.allowedAutorotateToLandscapeRight = v);
            Capture(() => PlayerSettings.Android.targetArchitectures, v => PlayerSettings.Android.targetArchitectures = v);
            Capture(() => PlayerSettings.Android.minSdkVersion, v => PlayerSettings.Android.minSdkVersion = v);
            Capture(() => PlayerSettings.Android.forceInternetPermission, v => PlayerSettings.Android.forceInternetPermission = v);
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                Capture(() => PlayerSettings.GetApplicationIdentifier(group), v => PlayerSettings.SetApplicationIdentifier(group, v));
                Capture(() => PlayerSettings.GetScriptingBackend(group), v => PlayerSettings.SetScriptingBackend(group, v));
                Capture(() => PlayerSettings.GetApiCompatibilityLevel(group), v => PlayerSettings.SetApiCompatibilityLevel(group, v));
            }
            Capture(() => EditorUserBuildSettings.development, v => EditorUserBuildSettings.development = v);
            Capture(() => EditorUserBuildSettings.buildScriptsOnly, v => EditorUserBuildSettings.buildScriptsOnly = v);
            Capture(() => EditorUserBuildSettings.exportAsGoogleAndroidProject, v => EditorUserBuildSettings.exportAsGoogleAndroidProject = v);
#if UNITY_EDITOR_WIN
            Capture(() => UnityEditor.WindowsStandalone.UserBuildSettings.createSolution, v => UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = v);
#endif
            Capture(() => EditorSettings.serializationMode, v => EditorSettings.serializationMode = v);
            Capture(() => EditorBuildSettings.scenes, v => EditorBuildSettings.scenes = v);
            string hybrid = EditorJsonUtility.ToJson(HybridCLRSettings.instance);
            restore.Add(() => { EditorJsonUtility.FromJsonOverwrite(hybrid, HybridCLRSettings.instance); HybridCLRSettings.Save(); });
        }
        public static BuildSourceTransaction Begin(IEnumerable<string> sourcePaths) => new BuildSourceTransaction(sourcePaths ?? Array.Empty<string>());
        private void Capture<T>(Func<T> read, Action<T> write) { T value = read(); restore.Add(() => write(value)); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var failures = new List<Exception>();
            foreach (Action action in restore) { try { action(); } catch (Exception error) { failures.Add(error); } }
            try { AssetDatabase.SaveAssets(); } catch (Exception error) { failures.Add(error); }
            try { files.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Build settings restoration failed; the build must not succeed.", failures);
        }
    }
}
