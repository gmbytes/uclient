using System;
using UnityEngine;
namespace Client.Framework.UI
{
    public sealed class UITextDefinition : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string Key; [TextArea] public string Value; }
        public Entry[] Entries=Array.Empty<Entry>();
    }
}
