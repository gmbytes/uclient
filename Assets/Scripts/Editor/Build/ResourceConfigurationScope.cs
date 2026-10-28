using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.Build;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;

namespace Client.Editor.Build
{
    // The sole project provider. UGF captures these values in readonly constructor fields.
    public static class ProjectResourceConfiguration
    {
        internal static ResourceConfigurationScope Active;
        [ResourceCollectionConfigPath] public static string CollectionPath => Active?.CollectionPath ?? DefaultCollectionPath;
        [ResourceBuilderConfigPath] public static string BuilderPath => Active?.BuilderPath ?? DefaultBuilderPath;
        public static string DefaultCollectionPath => Path.GetFullPath(Path.Combine(Application.dataPath, "GameFramework/Configs/ResourceCollection.xml"));
        public static string DefaultBuilderPath => Path.GetFullPath(Path.Combine(Application.dataPath, "GameFramework/Configs/ResourceBuilder.xml"));
    }

    public sealed class ResourceConfigurationScope : IDisposable
    {
        private readonly SourceFileTransaction transaction;
        private bool disposed;
        public string CollectionPath { get; }
        public string BuilderPath { get; }
        private ResourceConfigurationScope(string root)
        {
            CollectionPath = Path.Combine(Path.GetFullPath(root), "ResourceCollection.xml");
            BuilderPath = Path.Combine(Path.GetFullPath(root), "ResourceBuilder.xml");
            transaction = SourceFileTransaction.Capture(new[] { CollectionPath, BuilderPath, ProjectResourceConfiguration.DefaultCollectionPath, ProjectResourceConfiguration.DefaultBuilderPath });
        }
        public static ResourceConfigurationScope Enter(string root)
        {
            if (ProjectResourceConfiguration.Active != null) throw new BuildFailedException("A resource configuration scope is already active.");
            AssertSoleProvider(typeof(ResourceCollectionConfigPathAttribute));
            AssertSoleProvider(typeof(ResourceBuilderConfigPathAttribute));
            var scope = new ResourceConfigurationScope(root);
            ProjectResourceConfiguration.Active = scope;
            return scope;
        }
        private static void AssertSoleProvider(Type attribute)
        {
            var providers = GameFramework.Utility.Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed)
                .SelectMany(t => t.GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => (m is PropertyInfo property && property.PropertyType == typeof(string) || m is FieldInfo field && field.FieldType == typeof(string)) && m.IsDefined(attribute, false)).ToArray();
            if (providers.Length != 1 || providers[0].DeclaringType != typeof(ProjectResourceConfiguration))
                throw new BuildFailedException("Ambiguous UGF configuration ownership: " + attribute.Name + "; use the authoritative project scope.");
        }
        public ResourceCollection CreateCollection()
        {
            RequireActive(); var collection = new ResourceCollection(); VerifyCapturedPath(collection, CollectionPath); return collection;
        }
        public ResourceBuilderController CreateBuilder()
        {
            RequireActive(); var builder = new ResourceBuilderController(); VerifyCapturedPath(builder, BuilderPath);
            var collection = typeof(ResourceBuilderController).GetField("m_ResourceCollection", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(builder);
            VerifyCapturedPath(collection, CollectionPath); return builder;
        }
        private void RequireActive() { if (disposed || ProjectResourceConfiguration.Active != this) throw new ObjectDisposedException(nameof(ResourceConfigurationScope)); }
        private static void VerifyCapturedPath(object controller, string expected)
        {
            string actual = controller?.GetType().GetField("m_ConfigurationPath", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller) as string;
            if (actual == null || !Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new BuildFailedException("The native UGF controller captured an unexpected configuration path.");
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { transaction.Dispose(); }
            finally { if (ProjectResourceConfiguration.Active == this) ProjectResourceConfiguration.Active = null; }
        }
    }
}
