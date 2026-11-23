using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Network;
using Tok.Client.V1;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--port" || args[2] != "--case" ||
                !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                return 2;
            string scenario = args[3];
            using var alarm = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            try
            {
                Console.WriteLine("READY " + port.ToString(CultureInfo.InvariantCulture));
                using var peer = await listener.AcceptTcpClientAsync(alarm.Token);
                using var stream = peer.GetStream();
                var prefix = new byte[4]; await ReadExactly(stream, prefix, alarm.Token);
                int length = FrameCodec.ReadLength(prefix); var framed = new byte[length + 4];
                Array.Copy(prefix, framed, 4); await ReadExactly(stream, framed.AsMemory(4), alarm.Token);
                var login = FrameCodec.Decode(framed);
                if (login.Version != 3 || login.Login == null || login.Login.RequestId == 0 ||
                    string.IsNullOrWhiteSpace(login.Login.Credential) == string.IsNullOrWhiteSpace(login.Login.ResumeToken)) return 3;
                Console.WriteLine("RECEIVED");
                if (scenario == "close") return 0;
                if (scenario == "timeout" || scenario == "cancel") return await WaitForClose(stream, alarm.Token);
                var response = new Frame { Version = 3, LoggedIn = new LoginResponse
                {
                    RequestId = login.Login.RequestId, PlayerId = "1", SessionId = "fixture-session", Epoch = 9007199254740993UL,
                    ResumeToken = "fixture-memory-token", ResumeTokenTtlMs = 9007199254740995UL
                } };
                if (scenario == "reject") response.LoggedIn.ErrorCode = 3;
                else if (scenario == "wrong-version") response.Version = 2;
                else if (scenario == "wrong-id") response.LoggedIn.RequestId++;
                else if (scenario == "unknown-code") response.LoggedIn.ErrorCode = 123;
                else if (scenario == "business") response = new Frame { Version = 3, Notify = new Notify { MessageId = 1 } };
                else if (scenario != "success" && scenario != "half-frame" && scenario != "idle-disconnect") return 2;
                var encoded = FrameCodec.Encode(response);
                if (scenario == "half-frame") { await stream.WriteAsync(encoded.AsMemory(0, 5), alarm.Token); return 0; }
                // Fragment the prefix and body to exercise the real TCP reader.
                await stream.WriteAsync(encoded.AsMemory(0, 2), alarm.Token);
                await stream.WriteAsync(encoded.AsMemory(2), alarm.Token);
                if (scenario == "idle-disconnect") { await Task.Delay(100, alarm.Token); return 0; }
                return await WaitForClose(stream, alarm.Token);
            }
            finally { listener.Stop(); }
        }
        catch (IOException) { return 0; } // Windows may report local close as RST.
        catch (SocketException) { return 0; }
        catch (Exception error) { Console.Error.WriteLine("Fixture failed: " + error.GetType().Name); return 4; }
    }
    private static async Task ReadExactly(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        while (!buffer.IsEmpty)
        {
            int read = await stream.ReadAsync(buffer, token);
            if (read == 0) throw new EndOfStreamException();
            buffer = buffer.Slice(read);
        }
    }
    private static async Task<int> WaitForClose(NetworkStream stream, CancellationToken token)
    {
        var next = new byte[1];
        // Any extra request/ACK is outside N3 and fails the fixture.
        return await stream.ReadAsync(next, token) == 0 ? 0 : 5;
    }
}
