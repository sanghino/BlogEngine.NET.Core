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

## Provider dati

Si sceglie con `BlogEngine:DataProvider` in `appsettings.json`: `Xml` (predefinito) oppure `Sqlite`.
Entrambi scrivono nella sottocartella `data` della cartella dati:

- **Xml**: un file per post (commenti inclusi) e per pagina in `data/blogs/{id blog}/`, più file singoli per
  categorie, impostazioni, permessi, utenti, ecc. Le scritture sono atomiche (file temporaneo poi sostituito).
- **Sqlite**: un unico database `data/blogengine.db`, aggiornato automaticamente all'avvio con le migrazioni.

Ogni modifica allo schema SQLite (`BlogDbContext`) richiede una nuova migrazione:

```
dotnet tool restore
dotnet ef migrations add NomeModifica --project BlogEngine.Data.Sqlite --startup-project BlogEngine.Data.Sqlite --output-dir Migrations
```

I test in `BlogEngine.Tests/Data` verificano che i due provider (e lo store in memoria dei test) si comportino
allo stesso modo: un nuovo provider deve superare la stessa suite (`StoreContractTests`).

## Temi e widget

I temi sono template Liquid letti dal disco a ogni modifica: si installano copiando la cartella in
`Custom/Themes` (o in `App_Data/themes`), i widget in `Custom/Widgets` (o in `App_Data/widgets`).
I template predefiniti, usati quando un tema non ridefinisce una pagina, sono in `Templates/`.
Guide: `BlogEngine.Web/Custom/Themes/README.md` e `BlogEngine.Web/Custom/Widgets/README.md`.

## Pubblicazione su Aruba

```
dotnet publish BlogEngine.Web -c Release -o publish
```

Caricare via FTP il contenuto di `publish` nella cartella `public` del sito. In produzione i dati vanno in
`../App_Data`, cioè la cartella `App_Data` allo stesso livello di `public` (impostazione `BlogEngine:DataPath`
in `appsettings.json`). L'applicazione crea da sola le sottocartelle `files`, `cache`, `keys`, `logs`, `themes`,
`widgets` ed `extensions`.

Nel pannello Aruba il sito va impostato su .NET Core (al posto di "Framework .NET 4.0") solo al momento del
passaggio alla nuova versione. Per aggiornare un sito già online caricare prima un file `app_offline.htm`,
sostituire i file e poi cancellarlo.

Se il sito non si avvia, impostare `stdoutLogEnabled="true"` in `web.config`: il log viene scritto in
`App_Data/logs`.
