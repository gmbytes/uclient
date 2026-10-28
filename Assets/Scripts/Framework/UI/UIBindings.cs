using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Framework.UI
{
    /// <summary>Explicit AOT component references; child components need no window logic.</summary>
    [DisallowMultipleComponent]
    public sealed class UIBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            public string Id;
            public Component Value;
            public string ComponentType;
            public bool Required = true;
        }

        [Serializable]
        public sealed class ArrayBinding
        {
            public string Id;
            public Component[] Values = Array.Empty<Component>();
            public string ComponentType;
            public bool Required = true;
        }

        [SerializeField] private Binding[] m_Bindings = Array.Empty<Binding>();
        [SerializeField] private ArrayBinding[] m_Arrays = Array.Empty<ArrayBinding>();
        public IReadOnlyList<Binding> Singles => m_Bindings;
        public IReadOnlyList<ArrayBinding> Arrays => m_Arrays;

        public void Configure(Binding[] singles, ArrayBinding[] arrays)
        {
            m_Bindings = singles ?? Array.Empty<Binding>();
            m_Arrays = arrays ?? Array.Empty<ArrayBinding>();
        }

        public Component Resolve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Binding ID is required.", nameof(id));
            foreach (var binding in m_Bindings)
                if (binding != null && string.Equals(binding.Id, id, StringComparison.Ordinal)) return binding.Value;
            throw new KeyNotFoundException("Unknown UI binding: " + id);
        }

        public IReadOnlyList<Component> ResolveMany(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Binding ID is required.", nameof(id));
            foreach (var binding in m_Arrays)
                if (binding != null && string.Equals(binding.Id, id, StringComparison.Ordinal))
                    return Array.AsReadOnly(binding.Values ?? Array.Empty<Component>());
            throw new KeyNotFoundException("Unknown UI array binding: " + id);
        }
    }
}
