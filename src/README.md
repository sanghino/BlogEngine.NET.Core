# BlogEngine.NET – versione .NET 8

Nuova soluzione ASP.NET Core 8. Il vecchio progetto Web Forms in `../BlogEngine` resta come riferimento fino al
completamento del porting. Lo stato delle funzionalità è in [`../docs/inventario-funzionalita.md`](../docs/inventario-funzionalita.md).

## Progetti

| Progetto | Ruolo |
|---|---|
| `BlogEngine.Abstractions` | Contratti pubblici per temi ed estensioni di terze parti (da mantenere stabili) |
| `BlogEngine.Core` | Dominio e servizi, senza dipendenze da ASP.NET |
| `BlogEngine.Data.Xml` | Provider dati XML |
| `BlogEngine.Data.Sqlite` | Provider dati SQLite |
| `BlogEngine.Web` | Unica applicazione web (Aruba consente una sola app .NET Core per sito) |
| `BlogEngine.Tests` | Test unitari e di integrazione |

## Comandi

```
dotnet build
dotnet test
dotnet run --project BlogEngine.Web
```

In sviluppo i dati vengono scritti in `BlogEngine.Web/App_Data` (escluso da git e dal publish).

## Pubblicazione su Aruba

```
dotnet publish BlogEngine.Web -c Release -o publish
```

Caricare via FTP il contenuto di `publish` nella cartella `public` del sito. In produzione i dati vanno in
`../App_Data`, cioè la cartella `App_Data` allo stesso livello di `public` (impostazione `BlogEngine:DataPath`
in `appsettings.json`). L'applicazione crea da sola le sottocartelle `files`, `cache`, `keys`, `logs`, `themes`
ed `extensions`.

Nel pannello Aruba il sito va impostato su .NET Core (al posto di "Framework .NET 4.0") solo al momento del
passaggio alla nuova versione. Per aggiornare un sito già online caricare prima un file `app_offline.htm`,
sostituire i file e poi cancellarlo.

Se il sito non si avvia, impostare `stdoutLogEnabled="true"` in `web.config`: il log viene scritto in
`App_Data/logs`.
