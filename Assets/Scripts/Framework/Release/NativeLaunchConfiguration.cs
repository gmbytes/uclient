using System;

namespace Client.Framework.Release
{
    /// <summary>Build-owned native values. Never populated from an incoming channel or release manifest.</summary>
    public sealed class NativeLaunchConfiguration
    {
        public PlayerIdentity Player { get; }
        public string EntryTypeName { get; }
        public string ReadyViewId { get; }
        public TimeSpan LocalReadyTimeout { get; }
        public NativeLaunchConfiguration(PlayerIdentity player, string entryTypeName = "Client.Game.Entry", string readyViewId = "UILogin", TimeSpan? localReadyTimeout = null)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            if (string.IsNullOrWhiteSpace(entryTypeName)) throw new ArgumentException("A native entry type is required.", nameof(entryTypeName));
            if (string.IsNullOrWhiteSpace(readyViewId)) throw new ArgumentException("An expected local view is required.", nameof(readyViewId));
            var timeout = localReadyTimeout ?? TimeSpan.FromSeconds(30);
            if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(localReadyTimeout));
            EntryTypeName = entryTypeName; ReadyViewId = readyViewId; LocalReadyTimeout = timeout;
        }
    }
}