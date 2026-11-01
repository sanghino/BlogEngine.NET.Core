namespace BlogEngine.Web.Hosting;

/// <summary>
/// Nel pacchetto pubblicato i file del sito (wwwroot, Custom, Templates...) stanno accanto all'eseguibile,
/// che su Aruba è nella sottocartella app\ della root: IIS però può avviarlo con la root come cartella
/// corrente. In quel caso la content root diventa la cartella dell'eseguibile. In sviluppo e nei test
/// (nessuna wwwroot nella cartella di build) resta quella predefinita.
/// </summary>
internal static class PublishedLayout
{
    public static string? ContentRoot()
    {
        var folder = AppContext.BaseDirectory;
        return Directory.Exists(Path.Combine(folder, "wwwroot")) ? folder : null;
    }
}
