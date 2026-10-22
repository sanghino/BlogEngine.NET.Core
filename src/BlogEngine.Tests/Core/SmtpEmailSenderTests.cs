using System.Net;
using System.Net.Sockets;
using System.Text;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogEngine.Tests.Core;

/// <summary>Invio via SMTP verso un piccolo server finto in ascolto su localhost.</summary>
[TestClass]
public sealed class SmtpEmailSenderTests
{
    [TestMethod]
    public async Task Send_DeliversMessageWithSubjectPrefixAndCredentials()
    {
        using var host = new TestBlogHost().AsAdmin();
        using var server = new FakeSmtpServer();
        var settings = await host.Settings.GetAsync();
        settings.Email = "blog@example.com";
        settings.SmtpServer = "127.0.0.1";
        settings.SmtpServerPort = server.Port;
        settings.EnableSsl = false;
        settings.SmtpUserName = "utente";
        settings.SmtpPassword = "segreta";
        settings.EmailSubjectPrefix = "Blog";
        await host.Settings.SaveAsync(settings);

        var sender = new SmtpEmailSender(host.Context, host.Settings, host.Get<IEventPublisher>(), NullLogger<SmtpEmailSender>.Instance);
        var error = await sender.SendAsync(new EmailMessage { To = ["lettore@example.com"], Subject = "Ciao", HtmlBody = "<p>Prova è</p>" });

        Assert.IsNull(error, error);
        var transcript = await server.Completed;
        StringAssert.Contains(transcript, "AUTH PLAIN");
        StringAssert.Contains(transcript, "RCPT TO:<lettore@example.com>");
        StringAssert.Contains(transcript, "Subject: Blog - Ciao");
    }

    [TestMethod]
    public async Task Send_UnreachableServer_ReturnsError()
    {
        using var host = new TestBlogHost().AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.Email = "blog@example.com";
        settings.SmtpServer = "127.0.0.1";
        settings.SmtpServerPort = FakeSmtpServer.FreePort();
        await host.Settings.SaveAsync(settings);

        var sender = new SmtpEmailSender(host.Context, host.Settings, host.Get<IEventPublisher>(), NullLogger<SmtpEmailSender>.Instance);
        Assert.IsNotNull(await sender.SendAsync(new EmailMessage { To = ["lettore@example.com"], Subject = "Ciao", HtmlBody = "x" }));
    }

    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeSmtpServer()
        {
            _listener.Start();
            Completed = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Tutto ciò che il client ha inviato.</summary>
        public Task<string> Completed { get; }

        public static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose() => _listener.Stop();

        private async Task<string> ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var transcript = new StringBuilder();

            await writer.WriteLineAsync("220 localhost ESMTP");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                transcript.AppendLine(line);
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 OK");
                    }

                    continue;
                }

                var command = line.Split(' ')[0].ToUpperInvariant();
                switch (command)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250 AUTH PLAIN");
                        break;
                    case "AUTH":
                        await writer.WriteLineAsync("235 OK");
                        break;
                    case "DATA":
                        inData = true;
                        await writer.WriteLineAsync("354 Go");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return transcript.ToString();
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }

            return transcript.ToString();
        }
    }
}
