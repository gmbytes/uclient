using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Framework.UI
{
    [CreateAssetMenu(menuName = "Client/UI/View Definitions")]
    public sealed class UIViewDefinitions : ScriptableObject
    {
        [SerializeField] private UIViewDefinition[] m_Definitions = Array.Empty<UIViewDefinition>();
        public IReadOnlyList<UIViewDefinition> Definitions => m_Definitions;
        public void Configure(UIViewDefinition[] definitions) => m_Definitions = definitions ?? Array.Empty<UIViewDefinition>();
    }
}
