using System.Text.RegularExpressions;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Sostituisce nel testo di post e pagine i segnaposto dei campi personalizzati, nella forma
/// [CUSTOMFIELD|POST|id|chiave|valore predefinito/] (anche PAGE e THEME), e rimuove i marcatori
/// [CUSTOMBLOCK...] / [/CUSTOMBLOCK] (porting di CustomFieldsParser).
/// </summary>
public sealed partial class CustomFieldTokensHandler(ICustomFieldService fields) : IEventHandler<ServingEvent<Post>>, IEventHandler<ServingEvent<Page>>
{
    /// <summary>Prima delle estensioni, così lavorano sul testo con i valori già inseriti.</summary>
    public int Order => -1000;

    public async Task HandleAsync(ServingEvent<Post> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Body = await ReplaceAsync(blogEvent.Body, cancellationToken).ConfigureAwait(false);

    public async Task HandleAsync(ServingEvent<Page> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Body = await ReplaceAsync(blogEvent.Body, cancellationToken).ConfigureAwait(false);

    private async Task<string> ReplaceAsync(string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("[CUSTOM", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        html = BlockRegex().Replace(html, string.Empty).Replace("[/CUSTOMBLOCK]", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var match in FieldRegex().Matches(html).DistinctBy(m => m.Value).ToList())
        {
            var (type, id, key, fallback) = (match.Groups["type"].Value, match.Groups["id"].Value, match.Groups["key"].Value, match.Groups["default"].Value);
            var field = (await fields.GetFieldsAsync(type.ToUpperInvariant(), id, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            html = html.Replace(match.Value, field?.Value ?? fallback, StringComparison.Ordinal);
        }

        return html;
    }

    [GeneratedRegex(@"\[CUSTOMFIELD\|(?<type>POST|PAGE|THEME)\|(?<id>[^|\]]*)\|(?<key>[^|\]]*)\|(?<default>[^\]]*?)/?\]", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"\[CUSTOMBLOCK[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();
}
