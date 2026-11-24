using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmailDeliveryTests;

/// <summary>Loopback only, synthetic protocol peer. Never relays or stores real mail.</summary>
public sealed class SmtpFixture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(12));
    private readonly Task run;
    public int Port { get; }
    public int Submissions { get; private set; }
    public string Message { get; private set; } = "";
    public SmtpFixture(string mode)
    {
        listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        run = Run(mode);
    }
    private async Task Run(string mode)
    {
        try
        {
            using var connection = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 loopback.invalid synthetic SMTP");
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                if (line.StartsWith("EHLO") || line.StartsWith("HELO"))
                    await writer.WriteAsync("250-loopback.invalid\r\n250-AUTH PLAIN\r\n250 SIZE 110000000\r\n");
                else if (line.StartsWith("AUTH")) await writer.WriteLineAsync(mode == "auth" ? "535 5.7.8 synthetic secret must not leak" : "235 2.7.0 synthetic authenticated");
                else if (line.StartsWith("MAIL FROM")) await writer.WriteLineAsync("250 2.1.0 sender ok");
                else if (line.StartsWith("RCPT TO")) await writer.WriteLineAsync(mode == "recipient" ? "550 5.1.1 synthetic rejection with private diagnostic" : "250 2.1.5 recipient ok");
                else if (line == "RSET") await writer.WriteLineAsync("250 reset");
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 finish with dot");
                    var body = new StringBuilder();
                    while (await reader.ReadLineAsync(stop.Token) is { } data && data != ".") body.AppendLine(data);
                    Message = body.ToString(); Submissions++;
                    if (mode == "unknown") return; // Server read the data, but acceptance reply is lost.
                    await writer.WriteLineAsync(mode == "reject" ? "554 5.7.1 synthetic message rejected" : "250 2.0.0 synthetic accepted");
                }
                else if (line == "QUIT")
                {
                    if (mode == "quit") return; // Acceptance already happened: closing cannot revoke it.
                    await writer.WriteLineAsync("221 goodbye"); return;
                }
                else await writer.WriteLineAsync("500 unsupported synthetic command");
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); await run; stop.Dispose();
    }
}

public class FaultyCleanup : System.Reflection.DispatchProxy
{
    private readonly MailKit.Net.Smtp.SmtpClient inner = new() { Timeout = 3000 };
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "DisconnectAsync") return Task.FromException(new IOException("synthetic quit failure"));
        if (method.Name == "Dispose") { inner.Dispose(); throw new IOException("synthetic dispose failure"); }
        return method.Invoke(inner, args);
    }
}
