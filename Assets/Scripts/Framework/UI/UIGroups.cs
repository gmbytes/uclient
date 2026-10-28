using System;
using System.Collections.Generic;

namespace Client.Framework.UI
{
    public static class UIGroups
    {
        public const string Background = "Background";
        public const string Page = "Page";
        public const string Popup = "Popup";
        public const string Loading = "Loading";
        public const string System = "System";
        private static readonly string[] names = { Background, Page, Popup, Loading, System };
        public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(names);

        public static int Depth(string group)
        {
            int depth = Array.IndexOf(names, group);
            if (depth < 0) throw new ArgumentException("Unknown UI group: " + group, nameof(group));
            return depth;
        }
    }
}
