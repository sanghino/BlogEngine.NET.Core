# Inventario funzionalità – porting a .NET 8

Checklist di accettazione del porting di BlogEngine.NET da .NET Framework 4.5 (Web Forms) ad ASP.NET Core 8.
Ogni voce indica dove si trova oggi nel vecchio codice (`BlogEngine/`) e dove andrà nella nuova soluzione (`src/`).

Legenda stato: ☐ da fare · ◐ in corso (logica di dominio pronta nel Core, manca la parte web) · ☑ completato · ✖ eliminato (con motivazione)

Vincoli che valgono per tutte le voci: una sola applicazione .NET Core per sito (Aruba), tutto ciò che viene scritto
a runtime sta in `App_Data` (accanto a `public`), temi ed estensioni installabili copiando una cartella.

---

## 1. Contenuti

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ◐ | Post: creazione, modifica, bozze, pubblicazione programmata, slug, descrizione, autore | `Post.cs`, `PostRepository`, `PostsController` | Core + API |
| ◐ | Pagine statiche, gerarchia padre/figlio, pagina iniziale personalizzata | `Page.cs`, `PageRepository`, `PagesController` | Core + API |
| ◐ | Categorie gerarchiche | `Category.cs`, `CategoriesController` | Core + API |
| ◐ | Tag e tag cloud | `TagRepository`, `Data/Services/TagCloud.cs` | Core + API + widget |
| ◐ | Campi personalizzati su post, pagine, profili | `CustomFieldRepository`, `CustomFieldsParser` | Core + API |
| ◐ | Cestino: eliminazione logica, ripristino, svuotamento | `TrashRepository`, `TrashController` | Core + API |
| ☐ | Allegati / enclosure nei post | `EnableEnclosures` | Core + feed |
| ☐ | Post correlati | `RelatedPostsBase`, `EnableRelatedPosts` | Core + tema |
| ◐ | Navigazione post precedente/successivo | `PostNavigationBase` | Tema |
| ☐ | Archivio (per mese, calendario) | `archive.aspx`, `PostCalendar`, `MonthList` | Pagina + widget |
| ☐ | Ricerca, anche nei commenti, con stop word | `Services/Search/Search.cs`, `search.aspx`, `StopWords.cs` | Core + pagina |
| ☐ | Paginazione elenchi | `Helpers/Pager.cs`, `PostPager` | Core + tema |
| ◐ | Rating dei post | `RatingHandler` (`rating.axd`), `EnableRating` | Endpoint |
| ☐ | Esempio di home page personalizzata | `FrontPageExample.aspx` | Pagina/template di esempio |

## 2. Commenti

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ◐ | Commenti annidati, moderazione, approvazione/rifiuto, spam | `Comment.cs`, `CommentsController` | Core + API |
| ◐ | Chiusura commenti dopo N giorni, sito web e nazione opzionali | `BlogSettings` | Core |
| ◐ | Filtri commenti configurabili (IP, email, parole) | `CommentFilterRepository`, `CommentFilterController` | Core + API |
| ◐ | Filtri antispam personalizzati (`ICustomFilter`), segnalazione errori | `Services/Messaging/Protection` | Abstractions (punto di estensione) |
| ☐ | Avatar (Gravatar, ecc.) | `Data/Services/Avatar.cs` | Core |
| ☐ | Provider commenti alternativi: Disqus, Facebook | `CommentProvider`, `Disqus*`, `Facebook*` | Tema / impostazioni |
| ✖ | CoComment | `IsCoCommentEnabled` | Servizio esterno chiuso da anni |

## 3. Utenti, ruoli e sicurezza

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Login, logout, cambio password, recupero password | `Account/*.aspx`, `Logout.cshtml` | Razor Pages + autenticazione via cookie |
| ☐ | Registrazione autonoma con ruolo iniziale | `register.aspx`, `EnableSelfRegistration` | Razor Pages |
| ☐ | Creazione di un blog alla registrazione | `create-blog.aspx`, `CreateBlogOnSelfRegistration` | Razor Pages |
| ☐ | Utenti e ruoli (store XML / SQLite) | `XmlMembershipProvider`, `XmlRoleProvider` | Store personalizzato + `PasswordHasher` |
| ◐ | Permessi granulari (`Rights`: ~40 diritti, 8 gruppi) | `Services/Security/Rights.cs`, `Right.cs` | Core + policy di autorizzazione |
| ◐ | Profili autore | `AuthorProfile.cs`, `Profiles.cs` | Core + API |
| ☐ | Gestione www / non-www | `WwwSubDomainModule`, `HandleWwwSubdomain` | Middleware |
| ☐ | SSL obbligatorio per MetaWeblog | `RequireSslMetaWeblogApi` | Endpoint |
| ☐ | Persistenza chiavi di cifratura su hosting condiviso | (nuovo) | ☑ `App_Data/keys` (Fase 1) |

## 4. Multi-blog

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ◐ | Più blog nella stessa applicazione (sottocartella o host) | `Blog.cs`, `BlogsController`, `App_Data/blogs` | Core + middleware di risoluzione del blog |
| ◐ | Blog principale che vede i contenuti dei blog figli | `Blog.cs` | Core |
| ☐ | Generatore di nuovi blog da modello | `Helpers/BlogGenerator.cs` | Core |

## 5. Amministrazione (AngularJS 1.3, mantenuto nella prima fase)

| Stato | Sezione | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Dashboard e statistiche | `dashboard/`, `DashboardController`, `StatsController` | Contratti JSON invariati |
| ☐ | Editor di post e pagine (TinyMCE, Summernote, bootstrap-wysiwyg) | `admin/app/editor`, `admin/editors` | Le pagine `.cshtml` dell'editor diventano Razor ASP.NET Core |
| ☐ | File manager, upload | `FileManagerController`, `UploadController` | File in `App_Data/files` |
| ☐ | Contenuti: post, pagine, categorie, tag, commenti, blog | `admin/app/content/*` | API |
| ☐ | Utenti, ruoli, profilo | `admin/app/security/*` | API |
| ☐ | Impostazioni: base, avanzate, commenti, email, feed, controlli | `admin/app/settings/*`, `SettingsController` | API |
| ◐ | Blogroll, ping service | `settings/controls`, `BlogRollController`, `PingServicesController` | API |
| ☐ | Strumenti di verifica (email, permessi cartelle) | `settings/tools`, `ToolsController` | API, integrati con la diagnostica |
| ☐ | Log | `LogsController`, `UnhandledExceptionLogger` | `App_Data/logs` |
| ☐ | Gestione temi, widget, estensioni (plugin) | `admin/app/custom/*`, `PackagesController`, `PackageExtraController` | Solo installazioni locali (cartella / upload zip) |
| ☐ | Impostazioni delle estensioni | `admin/Extensions/Settings.aspx` | Razor Page o vista AngularJS |
| ☐ | Lookup (culture, fusi orari, editor…) | `LookupsController` | API |
| ☐ | News feed BlogEngine in dashboard | `NewsFeedController` | Da verificare: la fonte esterna potrebbe non esistere più |
| ✖ | Galleria online di temi ed estensioni | `Services/Packaging/Gallery*`, NuGet.Core | Servizio online non più disponibile |

## 6. Temi e widget

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Motore dei temi installabili da cartella | `.master` + `.ascx` in `Custom/Themes` | Template Liquid (Fluid) in `Custom/Themes` e `App_Data/themes` |
| ☐ | Tema `Standard` | `Custom/Themes/Standard` | Conversione in Liquid |
| ☐ | Tema `Standard-2017` | `Custom/Themes/Standard-2017` | Conversione in Liquid |
| ☐ | Tema `RazorHost` | `Custom/Themes/RazorHost` | Conversione in Liquid |
| ☐ | Tema dell'amministrazione | `admin/themes/standard` | Statico |
| ☐ | Tema mobile alternativo | `MobileThemeSwitch.cs` | Da decidere: con temi responsive potrebbe non servire |
| ☐ | Zone dei widget, ordinamento, configurazione | `WidgetZone`, `WidgetContainer`, `WidgetsController` | Core + motore temi |
| ☐ | Widget installabili da cartella (`widget.xml` + vista + edit) | `Custom/Widgets/*` | Template Liquid |
| ☐ | Widget inclusi: Administration, AuthorList, BlogList, BlogRoll, CategoryList, CommentList, LinkList, MonthList, Newsletter, PageList, PostList, Search, TagCloud, TextBox | `Custom/Widgets` | Conversione |
| ☐ | Menu delle pagine, casella di ricerca | `PageMenu`, `SearchBox`, `SearchOnSearch` | Tag/filtri Liquid |
| ☐ | Header HTML personalizzato, script di tracking | `HtmlHeader`, `TrackingScript` | Layout |
| ☐ | Titoli con nome del blog | `UseBlogNameInPageTitles` | Layout |

## 7. Estensioni

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Gestore estensioni: attivazione, priorità, impostazioni | `Web/Extensions/ExtensionManager.cs`, `ExtensionSettings` | Core + loader |
| ☐ | Estensioni in sorgente `.cs` compilate a runtime | (nuovo: oggi compilate col sito) | Roslyn, cache in `App_Data/cache` |
| ☐ | Estensioni in DLL precompilate | (nuovo) | `AssemblyLoadContext` |
| ◐ | Eventi del ciclo di vita su post, pagine, commenti, categorie, ricerca, email, referrer… | eventi statici (`Serving`, `Saving`, `Saved`, `AddingComment`, `CommentAdded`, `Publishing`, `Searching`, `EmailSent`, `SpamAttack`, …) | Abstractions: eventi/hook stabili |
| ☐ | AkismetFilter | `Custom/Extensions/AkismetFilter.cs` | Plugin |
| ☐ | BBCode | `BBCode.cs` | Plugin |
| ☐ | BreakPost | `BreakPost.cs` | Plugin |
| ☐ | Logger | `Logger.cs` | Plugin |
| ☐ | MediaElementPlayer | `MediaElementPlayer.cs` | Plugin |
| ☐ | Recaptcha | `Recaptcha/` | Plugin (API reCAPTCHA attuale) |
| ☐ | ResolveLinks | `ResolveLinks.cs` | Plugin |
| ☐ | SendCommentMail | `SendCommentMail.cs` | Plugin |
| ☐ | SendNewsletters | `SendNewsletters.cs` | Plugin |
| ☐ | SendPings | `SendPings.cs` | Plugin |
| ☐ | SimpleCaptcha | `SimpleCaptcha/` | Plugin |
| ☐ | Smilies | `Smilies.cs` | Plugin |
| ☐ | StopForumSpam | `StopForumSpam.cs` | Plugin |
| ◐ | Datastore delle impostazioni delle estensioni | `Web/Extensions/DataStore`, `XmlProvider/DataStore.cs` | Provider dati |

## 8. Endpoint pubblici e protocolli (URL `*.axd` invariati)

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Feed RSS 2.0 / Atom, per categoria, tag, autore, commenti | `SyndicationHandler` (`syndication.axd`), `SyndicationGenerator` | Endpoint |
| ☐ | Feed alternativo (es. FeedBurner), numero voci | `AlternateFeedUrl`, `PostsPerFeed` | Endpoint |
| ☐ | Sitemap XML | `SiteMap` (`sitemap.axd`) | Endpoint |
| ☐ | MetaWeblog / XML-RPC (Windows Live Writer, Open Live Writer) | `API/MetaWeblog` (`metaweblog.axd`), `wlwmanifest.xml`, `AppCode/Wlw` | Endpoint |
| ☐ | RSD | `RsdHandler` (`rsd.axd`) | Endpoint |
| ☐ | Pingback e trackback, in ricezione e invio | `PingbackHandler`, `TrackbackHandler`, `Services/Messaging/Ping` | Endpoint + servizio |
| ☐ | OpenSearch | `OpenSearchHandler` (`opensearch.axd`) | Endpoint |
| ☐ | OPML (blogroll) | `OpmlHandler` (`opml.axd`) | Endpoint |
| ☐ | APML, FOAF, SIOC | `Apml`, `Foaf`, `Sioc` | Endpoint |
| ☐ | Export BlogML | `BlogMLExportHandler` (`blogml.axd`) | Endpoint |
| ☐ | Import BlogML | `Services/Syndication/BlogML` | Admin |
| ☐ | File e immagini serviti con controllo accessi, ridimensionamento | `FileHandler` (`file.axd`), `ImageHandler` (`image.axd`) | Endpoint; immagini con ImageSharp/SkiaSharp |
| ☐ | Risorse localizzate per JavaScript | `ResourceHandler` (`*.res.axd`) | Endpoint |
| ☐ | JavaScript minificato | `JavaScriptHandler` (`*.js.axd`), AjaxMin | Minificazione in fase di build |
| ☐ | `robots.txt` | file statico | `wwwroot` |
| ☐ | Download di file remoti da parte del server | `RemoteFile.cs`, `AllowServerToDownloadRemoteFiles` | Core (`HttpClient`) |

## 9. URL e routing

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Permalink di post (con o senza data), pagine, categorie, tag, autori, calendario, contatti, archivio, ricerca | `UrlRewrite` + `UrlRules.cs` | Routing ASP.NET Core |
| ☐ | URL senza `.aspx` e redirect da quelli vecchi | `RedirectToRemoveFileExtension` | Routing + redirect |
| ☐ | Pagine di errore e 404 personalizzate | `error.aspx`, `error404.aspx`, `ErrorTitle`, `ErrorText` | Middleware di gestione errori |
| ◐ | Tracciamento referrer | `ReferrerModule`, `Referrer.cs` | Middleware |

## 10. Email, contatti, newsletter

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Invio email SMTP (SSL, prefisso oggetto) | `Utils.SendMailMessage`, `BlogSettings` | Core (MailKit o `SmtpClient`) |
| ☐ | Form contatti con allegati e reCAPTCHA | `contact.aspx` | Pagina + tema |
| ☐ | Notifica email dei commenti | estensione SendCommentMail | Plugin |
| ☐ | Newsletter | widget Newsletter + estensione SendNewsletters | Plugin + widget |

## 11. Localizzazione e impostazioni generali

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☐ | Interfaccia in 37 lingue | `App_GlobalResources/labels.*.resx` | Resx in Core + `IStringLocalizer` |
| ◐ | Cultura e fuso orario del blog | `Culture`, `TimeZoneId`, `BlogCulture.cs` | Middleware di localizzazione |
| ☐ | Calendario persiano | `AppCode/PersianCulture.cs` | Core |
| ◐ | ~90 impostazioni del blog | `BlogSettings.cs` | Core, stessi nomi |
| ☐ | Compressione delle risposte | `CompressionModule`, `CompressWebResource` | Middleware integrato |
| ☐ | Bundling/minificazione | `EnableOptimization`, System.Web.Optimization | Build |
| ◐ | Cache | `Providers/CacheProvider` | `IMemoryCache` |

## 12. Dati e installazione

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Provider XML | `Providers/XmlProvider` | `BlogEngine.Data.Xml` |
| ☑ | Provider SQLite | `setup/SQLite` + `DbBlogProvider` | `BlogEngine.Data.Sqlite` (EF Core) |
| ☐ | File system dei file caricati | `XmlFileSystemProvider` | Disco, in `App_Data/files` |
| ☐ | Configurazione iniziale via browser (provider, admin) | `SetupController`, `setup/` | Razor Page |
| ☐ | Pagina di diagnostica (permessi, versione runtime, percorsi) | (nuovo) | Razor Page interna |
| ☑ | Percorso dati configurabile, cartelle create all'avvio | (nuovo) | `BlogEnginePaths` (Fase 1) |
| ✖ | Provider SQL Server, MySQL, SQL CE | `setup/SQLServer`, `MySQL`, `SQL_CE`, `DbMembershipProvider` | Non richiesti |
| ✖ | File system su database o UNC | `DbFileSystemProvider`, `UNCFileSystemProvider`, LINQ to SQL | Non richiesti |
| ✖ | Script di upgrade e supporto Mono | `setup/upgrade`, `setup/Mono` | Installazione nuova; .NET 8 è già multipiattaforma |
| ✖ | Expression Builder di Web Forms | `Services/Compilation` | Specifici di Web Forms |
