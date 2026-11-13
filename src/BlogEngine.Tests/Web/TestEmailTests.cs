using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BlogEngine.Core.Services;
using BlogEngine.Web.Hosting;
using MailKit.Security;

namespace BlogEngine.Tests.Web;

/// <summary>
/// Pulsante "Prova" delle impostazioni email: usa lo stesso invio delle email vere (MailKit). Prima usava
/// System.Net.Mail.SmtpClient, che non supporta l'SSL diretto sulla porta 465 di smtps.aruba.it e con Aruba
/// rispondeva sempre "Failure sending mail".
/// </summary>
[TestClass]
public sealed class TestEmailTests
{
    [TestMethod]
    public void Security_Port465UsesDirectSsl_OtherPortsStartTls()
    {
        Assert.AreEqual(SecureSocketOptions.SslOnConnect, SmtpEmailSender.Security(true, 465));
        Assert.AreEqual(SecureSocketOptions.StartTls, SmtpEmailSender.Security(true, 587));
        Assert.AreEqual(SecureSocketOptions.None, SmtpEmailSender.Security(false, 465));
    }

    [TestMethod]
    public async Task TestEmail_IsDeliveredWithTheFormSettings()
    {
        using var smtp = new FakeSmtpServer();
        var serverTask = smtp.ReceiveOneAsync();

        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        var settings = JsonNode.Parse(await admin.GetStringAsync("/api/settings"))!["Settings"]!.AsObject();
        settings["Email"] = "blog@example.com";
        settings["SmtpServer"] = "127.0.0.1";
        settings["SmtpServerPort"] = smtp.Port;
        settings["SmtpUserName"] = "";
        settings["SmtpPassword"] = "";
        settings["EnableSsl"] = false;

        var response = await admin.PutAsync("/api/settings?action=testEmail", new StringContent(settings.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(string.Empty, (await response.Content.ReadAsStringAsync()).Trim('"'), "Nessun errore restituito.");

        var data = await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
        StringAssert.Contains(data, "RCPT TO:<blog@example.com>");
        StringAssert.Contains(data, "Success");
    }

    [TestMethod]
    public async Task TestEmail_UnreachableServer_ReturnsTheReason()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        var settings = JsonNode.Parse(await admin.GetStringAsync("/api/settings"))!["Settings"]!.AsObject();
        settings["Email"] = "blog@example.com";
        settings["SmtpServer"] = "127.0.0.1";
        settings["SmtpServerPort"] = FakeSmtpServer.FreePort();
        settings["EnableSsl"] = true;

        var response = await admin.PutAsync("/api/settings?action=testEmail", new StringContent(settings.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue((await response.Content.ReadAsStringAsync()).Trim('"').Length > 0, "Il motivo dell'errore arriva all'amministrazione.");
    }

    /// <summary>Server SMTP minimo in chiaro che accetta un messaggio e ne restituisce la conversazione.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeSmtpServer() => _listener.Start();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public async Task<string> ReceiveOneAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var log = new StringBuilder();
            await writer.WriteLineAsync("220 localhost ESMTP");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                log.AppendLine(line);
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
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "DATA":
                        inData = true;
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return log.ToString();
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }

            return log.ToString();
        }

        public void Dispose() => _listener.Stop();
    }
}
