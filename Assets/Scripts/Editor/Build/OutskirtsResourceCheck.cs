using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class OutskirtsResourceCheck
    {
        public static void Run()
        {
            const string directory = "Builds/Outskirts/";
            Directory.CreateDirectory(directory);
            string output = Path.GetFullPath(directory + "AndroidPackage-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string[] profiles = { "Assets/Settings/Environment/LocalSimulationProfile.asset", "Assets/Settings/Environment/DevelopmentProfile.asset" };
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Run this check outside Play Mode with Android active.");
                Directory.CreateDirectory(Path.Combine(output, "Config"));
                File.WriteAllText(directory + "package-result.txt", "RUNNING " + output);
                using (SourceFileTransaction.Capture(profiles))
                using (var scope = ResourceConfigurationScope.Enter(Path.Combine(output, "Config")))
                {
                    var request = new BuildRequest(BuildKind.Player, BuildPlatform.Android, output);
                    var result = new BuildResult(output);
                    ResourcePackageBuilder.Build(request, result, scope, Path.Combine(output, "Baseline"));
                    if (ShaderUtil.ShaderHasError(Shader.Find("Client/OutskirtsSurface")))
                        throw new InvalidOperationException("Outskirts shader compilation failed.");
                    if (ShaderUtil.ShaderHasError(Shader.Find("Hidden/Client/OutskirtsPostProcess")))
                        throw new InvalidOperationException("Outskirts post-process shader compilation failed.");
                }
                File.WriteAllText(directory + "package-result.txt", "OK " + output);
                Debug.Log("OUTSKIRTS_ANDROID_PACKAGE_OK " + output);
            }
            catch (Exception error)
            {
                File.WriteAllText(directory + "package-result.txt", error.ToString());
                Debug.LogException(error);
                throw;
            }
            finally
            {
                foreach (string profile in profiles) AssetDatabase.ImportAsset(profile, ImportAssetOptions.ForceUpdate);
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
    }
}
