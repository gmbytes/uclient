using System;

namespace Client.Framework.Release
{
    public static class ReleaseCompatibility
    {
        public static void RequireExact(PlayerIdentity release, PlayerIdentity player)
        {
            if (release == null || player == null ||
                !string.Equals(release.PlayerBuildId, player.PlayerBuildId, StringComparison.Ordinal) ||
                !string.Equals(release.Platform, player.Platform, StringComparison.Ordinal) ||
                !string.Equals(release.Architecture, player.Architecture, StringComparison.Ordinal) ||
                !string.Equals(release.UnityVersion, player.UnityVersion, StringComparison.Ordinal) ||
                !string.Equals(release.ContractVersion, player.ContractVersion, StringComparison.Ordinal) ||
                !string.Equals(release.EnvelopeSha256, player.EnvelopeSha256, StringComparison.Ordinal) ||
                !string.Equals(release.AotMetadataSetId, player.AotMetadataSetId, StringComparison.Ordinal))
                throw new ReleaseVerificationException(ReleaseErrorCode.IncompatiblePlayer);
        }
    }
}
