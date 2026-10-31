using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Client.Contracts;
using Client.Framework.Release;

namespace Client.Framework.HotUpdate
{
    public sealed class HotfixAssemblyLoader
    {
        private static int attempted;
        private readonly string entryTypeName;

        public HotfixAssemblyLoader(string entryTypeName)
        {
            this.entryTypeName = !string.IsNullOrWhiteSpace(entryTypeName) ? entryTypeName : throw new ArgumentException("An entry type name is required.", nameof(entryTypeName));
        }

        public IHotfixEntry LoadOnce(PreparedRelease prepared, PlayerIdentity player)
        {
            BeginAttempt();
            var plan = HotfixLoadPlan.Create(prepared, player);
            ValidateAssemblies(plan.Assemblies, plan.ReferenceFacades.Concat(plan.Metadata.Names));
            new AotMetadataLoader().Load(plan.Metadata);
            return LoadAssemblies(plan.Assemblies);
        }

#if UNITY_EDITOR || MINIMAL_HOTFIX_PROOF
        // F3 harness only; absent from production Players.
        public IHotfixEntry LoadOnce(IReadOnlyList<VerifiedAssembly> assemblies)
        {
            BeginAttempt();
            ValidateAssemblies(assemblies);
            return LoadAssemblies(assemblies);
        }
#endif
        private static void BeginAttempt()
        {
            // 即使中途失败也不清除此标记：程序集无法可靠卸载，本进程不能再尝试另一组热代码。
            if (Interlocked.Exchange(ref attempted, 1) != 0)
                throw new InvalidOperationException("A hotfix load was already attempted in this process; restart is required.");
        }
        private static void ValidateAssemblies(IReadOnlyList<VerifiedAssembly> assemblies, IEnumerable<string> boundAotReferences = null)
        {
            if (assemblies == null || assemblies.Count == 0) throw new ArgumentException("A complete assembly set is required.", nameof(assemblies));
            var available = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), StringComparer.Ordinal);
            if (boundAotReferences != null) available.UnionWith(boundAotReferences);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in assemblies)
            {
                if (assembly == null || !names.Add(assembly.Name)) throw new ArgumentException("Duplicate or null hotfix assembly.", nameof(assemblies));
                if (available.Contains(assembly.Name)) throw new ArgumentException("Hotfix assembly is already loaded.", nameof(assemblies));
                if (assembly.Dependencies.Any(d => !available.Contains(d))) throw new ArgumentException("Assembly dependencies are not in load order.", nameof(assemblies));
                available.Add(assembly.Name);
            }

        }

        private IHotfixEntry LoadAssemblies(IReadOnlyList<VerifiedAssembly> assemblies)
        {
            Type entryType = null;
            foreach (var verified in assemblies)
            {
                var assembly = Assembly.Load(verified.Bytes);
                if (assembly.GetName().Name != verified.Name) throw new InvalidOperationException("Loaded assembly name does not match its descriptor.");
                var candidate = assembly.GetType(entryTypeName, false, false);
                if (candidate == null) continue;
                if (entryType != null) throw new InvalidOperationException("The entry type is ambiguous.");
                entryType = candidate;
            }
            if (entryType == null || !typeof(IHotfixEntry).IsAssignableFrom(entryType) || entryType.IsAbstract)
                throw new InvalidOperationException("The configured entry does not implement the stable hotfix contract.");
            return (IHotfixEntry)Activator.CreateInstance(entryType);
        }
    }
}
