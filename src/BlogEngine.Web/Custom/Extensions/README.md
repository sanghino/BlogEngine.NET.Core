# Estensioni di BlogEngine

Un'estensione è una cartella con uno o più file sorgente C# (`.cs`) ed eventuali librerie `.dll`.
Il sito la compila da solo all'avvio: non serve Visual Studio e sul server non va installato nulla.

## Dove si mettono

- `Custom/Extensions/<Nome>` – estensioni incluse nel pacchetto o copiate via FTP;
- `App_Data/extensions/<Nome>` – estensioni caricate (eliminabili dall'amministrazione).

Quando un file `.cs` o `.dll` viene aggiunto, modificato o tolto, il sito si riavvia da solo entro
pochi secondi e ricompila (come succedeva con App_Code). Gli assembly compilati restano in
`App_Data/cache/extensions`: gli avvii successivi non ricompilano nulla.

Un'estensione con errori non blocca il sito: compare in *Personalizza → Estensioni* con il messaggio
di errore (file e riga) e viene ignorata. Gli errori sono scritti anche in `App_Data/logs`.

## Esempio

```csharp
// Custom/Extensions/Firma/Firma.cs
[Extension("Aggiunge una firma in fondo ai post", "1.0", "Mario Rossi")]
public sealed class Firma : IBlogExtension
{
    // Registra i componenti: gestori di eventi, filtri antispam, captcha, dati per i widget...
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<ServingEvent<Post>>, FirmaHandler>();

    // Impostazioni modificabili da Personalizza → Estensioni → Personalizza.
    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition("Firma") { IsScalar = true }
            .AddParameter("Testo", "Testo della firma", 200, required: true)
            .SetDefault("Testo", "-- Mario"),
    ];
}

public sealed class FirmaHandler(IExtensionSettingsService settings) : IEventHandler<ServingEvent<Post>>
{
    public async Task HandleAsync(ServingEvent<Post> e, CancellationToken cancellationToken)
    {
        if (e.Location == ServingLocation.SinglePost)
        {
            var testo = (await settings.GetAsync("Firma", cancellationToken: cancellationToken)).Get("Testo");
            e.Body += $"<p class=\"firma\">{System.Net.WebUtility.HtmlEncode(testo)}</p>";
        }
    }
}
```

Il nome dell'estensione è il nome della classe. Sono già disponibili le using più comuni (`System`,
`System.IO`, `System.Linq`, `BlogEngine.Events`, `BlogEngine.Models`, `BlogEngine.Services`,
`BlogEngine.Extensions`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`).

## Cosa può fare un'estensione

| Componente | A cosa serve |
|------------|--------------|
| `IEventHandler<T>` | reagire agli eventi: `ServingEvent<Post/Page/Comment>` (modificare il testo mostrato), `SavingEvent<T>`/`SavedEvent<T>`, `PostPublishedEvent`, `CommentChangingEvent` (annullabile), `CommentChangedEvent`, `PostRatedEvent`... |
| `ICommentSpamFilter` | filtro antispam per i nuovi commenti (come Akismet) |
| `ICaptchaProvider` | captcha nei moduli dei commenti e dei contatti |
| `IWidgetDataProvider` | dati per un widget (vedi `Custom/Widgets/README.md`) |

Servizi utili da chiedere nel costruttore: `IExtensionSettingsService` (impostazioni), `IPageResources`
(script e stili da aggiungere alla pagina), `IEmailQueue` (email in background), `ILabels`
(traduzioni), `ILinkGenerator` (indirizzi), `ISettingsService`, `IPostService`, `ICommentService`,
`IHttpClientFactory`, `ILogger<T>`.

Quando l'estensione è disattivata dall'amministrazione i suoi componenti vengono ignorati subito,
senza riavvio. La priorità decide l'ordine rispetto alle altre estensioni (valori bassi prima).

## Impostazioni

`ExtensionSettingsDefinition` descrive un gruppo di impostazioni: valori singoli (`IsScalar = true`)
oppure una tabella con più righe (come i codici di BBCode). Tipi dei campi: `String`, `Boolean`,
`Integer`, `Long`, `Float`, `Double`, `Decimal`, `DropDown`, `ListBox`, `RadioGroup` (le opzioni sono
`"valore"` oppure `"valore|testo"`). La pagina di modifica viene generata automaticamente; in
alternativa l'attributo `[Extension]` accetta `AdminPage = "~/..."` per una pagina propria.

## Note per chi converte una vecchia estensione

- niente costruttori statici con `Post.Serving += ...`: si registra un `IEventHandler<ServingEvent<Post>>`;
- niente `ExtensionManager.GetSettings`: si usa `IExtensionSettingsService`;
- niente `HttpContext.Current`: si chiede `IHttpContextAccessor` nel costruttore;
- `[GeneratedRegex]` non è disponibile (la compilazione al volo non esegue i generatori): si usa `new Regex(...)`.
