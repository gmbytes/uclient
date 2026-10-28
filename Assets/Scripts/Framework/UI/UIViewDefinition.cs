using System;

namespace Client.Framework.UI
{
    [Serializable]
    public sealed class UIViewDefinition
    {
        public string ViewID;
        public string PrefabAddress;
        public string Group = UIGroups.Page;
        public bool Modal;
        public bool CloseOnMask;
        public AdaptiveLayoutDefinition LayoutDefinition;
    }
}
