using System.ServiceModel.Syndication;
using System.Xml;
using BlogEngine.Core.Configuration;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Verifiche dell'ambiente nella pagina "Strumenti" dell'amministrazione.</summary>
[Route("api/tools")]
public sealed class ToolsController(BlogEnginePaths paths, IRightsService rights) : AdminApiController
{
    /// <summary>
    /// "check1": identità del processo; "trust": livello di fiducia (in .NET 8 sempre completo);
    /// "root", "data", "Custom": prova a scrivere e cancellare un file nella cartella corrispondente.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages))
        {
            throw new UnauthorizedAccessException();
        }

        switch (id)
        {
            case "check1":
                return Ok($"ASP.NET application Identity is: {Environment.UserDomainName}\\{Environment.UserName}");
            case "trust":
                return Ok(new { success = true, msg = "Trust level: Full (.NET 8)" });
        }

        var folder = id.ToLowerInvariant() switch
        {
            "root" => paths.ContentRoot,
            "data" => paths.DataRoot,
            "custom" => Path.Combine(paths.ContentRoot, "Custom"),
            _ => null,
        };

        if (folder is null)
        {
            return NotFound();
        }

        var file = Path.Combine(folder, $"tmp-{Guid.NewGuid():N}.txt");
        try
        {
            Directory.CreateDirectory(folder);
            await System.IO.File.WriteAllTextAsync(file, "*-*-*");
            System.IO.File.Delete(file);
            return Ok(new { success = true, msg = $"Can create and delete file {file}" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Ok(new { success = false, msg = $"Error creating file {file}: {ex.Message}" });
        }
    }
}

[Route("api/logs")]
public sealed class LogsController(FileLog log, IRightsService rights) : AdminApiController
{
    [HttpGet("getlog/{id}")]
    public async Task<IEnumerable<SelectOption>> GetLog(string id)
    {
        await DemandAsync();
        return id == "file"
            ? log.ReadEntries().Select((entry, i) => new SelectOption { OptionName = $"Line{i + 1}", OptionValue = entry }).ToList()
            : [];
    }

    [HttpPut("purgelog/{id?}")]
    public async Task<IActionResult> PurgeLog()
    {
        await DemandAsync();
        log.Clear();
        return Ok();
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

/// <summary>
/// Notizie nella dashboard. Il feed originale (blogengine.io/news.xml) può non essere più disponibile:
/// in quel caso l'elenco è semplicemente vuoto.
/// </summary>
[Route("api/newsfeed")]
public sealed class NewsFeedController(IHttpClientFactory httpClients, ILogger<NewsFeedController> logger) : AdminApiController
{
    public const string FeedUrl = "https://blogengine.io/news.xml";

    [HttpGet("")]
    public async Task<List<SelectOption>> Get()
    {
        try
        {
            using var client = httpClients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            await using var stream = await client.GetStreamAsync(FeedUrl, HttpContext.RequestAborted);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true });
            return SyndicationFeed.Load(reader).Items
                .Take(6)
                .Select(i => new SelectOption { OptionName = i.Title?.Text, OptionValue = i.Id })
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            logger.LogInformation("Feed delle notizie non disponibile: {Message}", ex.Message);
            return [];
        }
    }
}
