namespace Client.Contracts
{
    public enum ClientLogLevel { Info, Warning, Error }

    public interface ILogService
    {
        void Write(ClientLogLevel level, string code, string message);
    }
}
