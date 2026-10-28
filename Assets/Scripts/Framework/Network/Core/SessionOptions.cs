using System;
namespace Client.Framework.Network
{
    public sealed class SessionOptions
    {
        public string Host { get; }
        public int Port { get; }
        public TimeSpan ConnectTimeout { get; }
        public TimeSpan LoginTimeout { get; }
        public SessionOptions(string host, int port, TimeSpan? connectTimeout = null, TimeSpan? loginTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A server host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Host = host; Port = port;
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
            LoginTimeout = loginTimeout ?? TimeSpan.FromSeconds(5);
            if (ConnectTimeout <= TimeSpan.Zero || LoginTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(connectTimeout), "Session deadlines must be positive.");
        }
    }
}
