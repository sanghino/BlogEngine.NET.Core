<p>
    <img src="src/BlogEngine.Web/wwwroot/Content/images/brand/logo.svg" alt="ExtraGeek-BlogEngine" width="420">
</p>

**ExtraGeek-BlogEngine** è una piattaforma per blog basata su ASP.NET Core: multilingua (italiano, inglese e altre lingue), con temi Liquid, widget ed estensioni compilate al volo, dati salvati in file XML o in SQLite. Funziona anche su un normale hosting Windows condiviso, ad esempio Aruba.

## Funzionalità principali

- Post, pagine, categorie, tag, commenti con moderazione, voti e newsletter.
- Più blog nella stessa installazione (blog figli).
- Tre temi inclusi: Standard, Standard-2017 e Terminal.
- Widget, spazi pubblicitari, banner per il consenso ai cookie e modalità manutenzione.
- Feed RSS e Atom, sitemap, MetaWeblog (Open Live Writer), importazione ed esportazione BlogML.

## Requisiti

- Per lo sviluppo: .NET SDK 8 o 10.
- Per la pubblicazione: hosting Windows con IIS. Il pacchetto per Aruba include già il runtime .NET.

## Sviluppo

```
cd src
dotnet run --project BlogEngine.Web
```

Al primo avvio il sito chiede la configurazione iniziale. La chiave da inserire si trova in `src/BlogEngine.Web/App_Data/setup-key.txt`. I test si avviano con `dotnet test` dalla cartella `src`.

La prima compilazione scarica da npm l'editor TinyMCE e la sua traduzione italiana (serve la connessione a Internet). Lo fa `tools/get-tinymce.ps1`, che fissa la versione e controlla l'impronta di ogni archivio.

## Pubblicazione

- [Guida alla pubblicazione su Aruba](docs/deploy-aruba.md) (pacchetto creato con `tools/publish-aruba.ps1`)
- [Checklist di collaudo](docs/checklist-collaudo.md)
- [Inventario delle funzionalità](docs/inventario-funzionalita.md)

Il logo si rigenera con `tools/brand/make_logo.py`. Usa il font Cascadia Mono, con licenza SIL OFL.

## Licenza

Codice rilasciato con licenza MS-RL. Il progetto deriva da BlogEngine.NET (Copyright 2007–2023 BlogEngine), distribuito con la stessa licenza.

L'editor TinyMCE (dalla versione 7 con licenza GPL) non fa parte del repository: viene scaricato durante la compilazione ed entra solo nei pacchetti dei siti.
