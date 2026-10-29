using Client.Contracts;
namespace Client.Game
{
    // A hot-only marker; acceptance overlays replace this source, never native trust or release state.
    public static class DeliveryVersion
    {
        public static void Run(ILogService log) => log?.Write(ClientLogLevel.Info,"CLIENT_CODE_VERSION","v1");
    }
}
