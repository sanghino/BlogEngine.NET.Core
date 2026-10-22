using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace BlogEngine.Web.Protocols;

/// <summary>
/// Scarica pagine di altri siti (per verificare pingback e trackback o trovare il loro indirizzo di
/// notifica). Per sicurezza si collega solo a indirizzi pubblici: un visitatore non può far aprire al
/// server indirizzi interni (127.0.0.1, rete locale...). Tempo e dimensione massima sono limitati.
/// </summary>
public interface IRemotePages
{
    /// <summary>Testo della pagina (al massimo 1 MB) e intestazioni, oppure null se non raggiungibile.</summary>
    Task<RemotePage?> GetAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>Invia un POST a un altro sito (pingback XML-RPC o trackback) e restituisce la risposta.</summary>
    Task<string?> PostAsync(Uri url, HttpContent content, CancellationToken cancellationToken);

    /// <summary>Scarica un file (es. un'immagine), oppure null se non raggiungibile o più grande di <paramref name="maxBytes"/>.</summary>
    Task<byte[]?> DownloadAsync(Uri url, long maxBytes, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class RemotePages(IHttpClientFactory httpClients) : IRemotePages
{
    public const string ClientName = "BlogEngine.RemotePages";
    private const int MaxBytes = 1024 * 1024;

    /// <summary>Registra il client HTTP con il controllo degli indirizzi.</summary>
    public static void Register(IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlogEngine.NET/4.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            ConnectCallback = ConnectToPublicAddressAsync,
        });
        services.AddSingleton<IRemotePages, RemotePages>();
    }

    public async Task<RemotePage?> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var response = await httpClients.CreateClient(ClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxBytes];
            var total = 0;
            int read;
            while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), cancellationToken)) > 0)
            {
                total += read;
            }

            var charset = response.Content.Headers.ContentType?.CharSet;
            var encoding = Encoding.UTF8;
            try
            {
                if (!string.IsNullOrEmpty(charset))
                {
                    encoding = Encoding.GetEncoding(charset.Trim('"'));
                }
            }
            catch (ArgumentException)
            {
                // Codifica sconosciuta: si usa UTF-8.
            }

            return new RemotePage(encoding.GetString(buffer, 0, total), response.Headers, response.RequestMessage?.RequestUri ?? url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<string?> PostAsync(Uri url, HttpContent content, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var response = await httpClients.CreateClient(ClientName).PostAsync(url, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return text.Length > MaxBytes ? text[..MaxBytes] : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<byte[]?> DownloadAsync(Uri url, long maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var response = await httpClients.CreateClient(ClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeoutSource.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublic)
            ?? throw new HttpRequestException($"L'indirizzo {context.DnsEndPoint.Host} non è pubblico.");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Esclude loopback, reti private, link-local e indirizzi riservati.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10
                || b[0] == 0
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || b[0] >= 224);
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);
    }
}

public sealed record RemotePage(string Html, HttpResponseHeaders Headers, Uri FinalUrl);
