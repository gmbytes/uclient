using System;
using System.Linq;
using UnityEngine;
namespace Client.Framework.UI
{
    /// <summary>Authoring locator; the catalog entry is the sole runtime definition.</summary>
    public sealed class UIWindowConfig : ScriptableObject
    {
        public UIViewDefinitions Catalog;
        public string ViewID;
        public UIViewDefinition Resolve()
        {
            if(Catalog==null) throw new InvalidOperationException("Missing UI catalog.");
            var matches=Catalog.Definitions.Where(d=>d!=null && d.ViewID==ViewID).ToArray();
            if(matches.Length!=1) throw new InvalidOperationException("Config must locate exactly one catalog entry: "+ViewID);
            return matches[0];
        }
    }
}
