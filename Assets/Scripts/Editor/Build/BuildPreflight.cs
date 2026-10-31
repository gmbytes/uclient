using System;
using System.IO;
using System.Linq;
using Client.Editor.UI;
using Client.Framework.UI;
using HybridCLR;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class BuildPreflight
    {
        public static void ValidateSigningKeyLocation(BuildRequest request)=>SigningKeyLocation.Require(request);
        public static BuildTarget Target(BuildPlatform platform)
        {
            switch (platform)
            {
                case BuildPlatform.Android: return BuildTarget.Android;
                case BuildPlatform.Windows64: return BuildTarget.StandaloneWindows64;
                default: throw new BuildFailedException("Only Android and Windows64 are supported.");
            }
        }

        public static void ValidateRequest(BuildRequest request)
        {
            if (request == null) throw new BuildFailedException("A build request is required.");
            if (request.Kind != BuildKind.Player && request.Kind != BuildKind.Patch) throw new BuildFailedException("Only Player and Patch build kinds are supported.");
            BuildTarget target = Target(request.Platform);
            if (string.IsNullOrWhiteSpace(request.OutputRoot)) throw new BuildFailedException("OutputRoot is required.");
            try
            {
                string output = Path.GetFullPath(request.OutputRoot);
                SourceFileTransaction.RejectReparsePoints(output);
                if (File.Exists(output)) throw new IOException("OutputRoot is a file.");
                if (request.Kind == BuildKind.Player)
                {
                    if (!string.IsNullOrWhiteSpace(request.PlayerArchive)) throw new BuildFailedException("PlayerArchive is only valid for a Patch request.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(request.PlayerArchive)) throw new BuildFailedException("Patch requires an explicit finalized PlayerArchive.");
                string archiveRoot = Path.GetFullPath(request.PlayerArchive);
                SourceFileTransaction.RejectReparsePoints(archiveRoot);
                if (!File.Exists(Path.Combine(archiveRoot, "archive.json"))) throw new BuildFailedException("Patch requires an existing finalized PlayerArchive.");
                if (output.Equals(archiveRoot, StringComparison.OrdinalIgnoreCase) || output.StartsWith(archiveRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException("Patch output cannot modify its immutable PlayerArchive.");
                var archive = PlayerBaselineArchive.Open(archiveRoot);
                if (archive.platform != target.ToString() || archive.architecture != (target == BuildTarget.Android ? "arm64" : "x64"))
                    throw new BuildFailedException("PlayerArchive platform or architecture does not match the requested Patch.");
            }
            catch (BuildFailedException) { throw; }
            catch (Exception error) { throw new BuildFailedException("Invalid build request or PlayerArchive: " + error.Message); }
        }

        public static void ValidateEnvironment(BuildRequest request)
        {
            ValidateRequest(request);
            BuildTarget target = Target(request.Platform);
            if (EditorUserBuildSettings.activeBuildTarget != target) throw new BuildFailedException("Launch Unity with the requested -buildTarget; active target differs.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target)) throw new BuildFailedException("Required build platform module is unavailable.");
            ValidateHotfixExclusion();
            ValidateScenes(EditorBuildSettings.scenes);
            new UIBindingValidator().OnPreprocessBuild(null);
            new UIAuthoringValidator().OnPreprocessBuild(null);
        }

        public static void ValidateHotfixExclusion()
        {
            if (!SettingsUtil.Enable) throw new BuildFailedException("HybridCLR must be enabled.");
            var actual = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(new[] { "Client.Game", "Client.Game.Protocol" })) throw new BuildFailedException("Both hot assemblies must be excluded from native AOT compilation.");
        }

        public static void ValidateScenes(EditorBuildSettingsScene[] scenes)
        {
            if (scenes == null || !scenes.Any(s => s != null && s.enabled)) throw new BuildFailedException("At least one Player scene is required.");
            if (scenes.Any(s => s != null && s.enabled && s.path.Replace('\\', '/') == UIWindowCreator.PreviewScenePath))
                throw new BuildFailedException("UIEdit is editor-only and cannot be included in a Player.");
        }

        public static void ValidateBindings(GameObject prefab, UIViewDefinition definition)
        {
            var errors = UIBindingValidator.Validate(prefab, definition);
            if (errors.Count != 0) throw new BuildFailedException("Invalid UI bindings: " + string.Join("\n", errors));
        }
    }
}
