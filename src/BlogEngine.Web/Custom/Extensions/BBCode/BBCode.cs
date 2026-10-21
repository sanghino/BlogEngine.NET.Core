// Converte i codici BBCode dei commenti ([b], [i], [u], [quote]...) in HTML.
// Porting dell'estensione BBCode di BlogEngine.NET.

[Extension("Converts BBCode to XHTML in the comments", "4.0", "<a href=\"https://blogengine.io\">BlogEngine.NET</a>")]
public sealed class BBCode : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<ServingEvent<Comment>>, BBCodeHandler>();

    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition(nameof(BBCode)) { Help = "Converts BBCode to XHTML in the comments. Close tag is optional." }
            .AddParameter("Code", "Code", 20, required: true, keyField: true)
            .AddParameter("OpenTag", "Open Tag", 150, required: true)
            .AddParameter("CloseTag", "Close Tag")
            .AddRow("b", "strong", "")
            .AddRow("i", "em", "")
            .AddRow("u", "span style=\"text-decoration:underline\"", "span")
            .AddRow("quote", "cite title=\"Quote\"", "cite"),
    ];
}

public sealed class BBCodeHandler(IExtensionSettingsService settings) : IEventHandler<ServingEvent<Comment>>
{
    public async Task HandleAsync(ServingEvent<Comment> blogEvent, CancellationToken cancellationToken)
    {
        var body = blogEvent.Body;
        if (string.IsNullOrEmpty(body) || !body.Contains('['))
        {
            return;
        }

        foreach (var row in (await settings.GetAsync(nameof(BBCode), cancellationToken: cancellationToken)).Rows)
        {
            var code = row.GetValueOrDefault("Code") ?? string.Empty;
            var open = row.GetValueOrDefault("OpenTag") ?? string.Empty;
            var close = row.GetValueOrDefault("CloseTag");
            if (code.Length > 0 && open.Length > 0)
            {
                body = Parse(body, code, open, string.IsNullOrEmpty(close) ? open : close);
            }
        }

        blogEvent.Body = body;
    }

    /// <summary>Sostituisce ogni coppia [code]...[/code] con i tag HTML indicati.</summary>
    private static string Parse(string body, string code, string startTag, string endTag)
    {
        var start = body.IndexOf($"[{code}]", StringComparison.Ordinal);
        while (start > -1 && body.IndexOf($"[/{code}]", start, StringComparison.Ordinal) > -1)
        {
            body = body.Remove(start, code.Length + 2).Insert(start, $"<{startTag}>");
            var end = body.IndexOf($"[/{code}]", start, StringComparison.Ordinal);
            body = body.Remove(end, code.Length + 3).Insert(end, $"</{endTag}>");
            start = body.IndexOf($"[{code}]", start, StringComparison.Ordinal);
        }

        return body;
    }
}
