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
| ☑ | Post: creazione, modifica, bozze, pubblicazione programmata, slug, descrizione, autore | `Post.cs`, `PostRepository`, `PostsController` | Core + API |
| ☑ | Pagine statiche, gerarchia padre/figlio, pagina iniziale personalizzata | `Page.cs`, `PageRepository`, `PagesController` | Core + API + `/page/{slug}`; home da `CustomFrontPage` o dalla pagina "home" (`/blog` mostra comunque i post) |
| ☑ | Categorie gerarchiche | `Category.cs`, `CategoriesController` | Core + API + `/category/{slug}` |
| ☑ | Tag e tag cloud | `TagRepository`, `Data/Services/TagCloud.cs` | Core + API + `/tag/{tag}` (anche `?tag=`) + widget TagCloud |
| ☑ | Campi personalizzati su post, pagine, profili e temi | `CustomFieldRepository`, `CustomFieldsParser` | Core + API; segnaposto `[CUSTOMFIELD...]` e `[CUSTOMBLOCK]` nei testi (`CustomFieldTokensHandler`), filtro `theme_field` nei temi |
| ☑ | Cestino: eliminazione logica, ripristino, svuotamento | `TrashRepository`, `TrashController` | Core + API |
| ☑ | Allegati / enclosure nei post | `EnableEnclosures` | Primo file audio/video del sito citato nel post, come `<enclosure>` nei feed |
| ☑ | Post correlati | `RelatedPostsBase`, `EnableRelatedPosts` | `BlogController` + `related-posts.liquid` |
| ☑ | Navigazione post precedente/successivo | `PostNavigationBase` | `post-navigation.liquid` |
| ☑ | Archivio (per categoria, per mese, calendario) | `archive.aspx`, `PostCalendar`, `MonthList` | `/archive`, `/calendar`, `/{anno}/{mese}/{giorno}`, widget MonthList |
| ☑ | Ricerca, anche nei commenti, con stop word | `Services/Search/Search.cs`, `search.aspx`, `StopWords.cs` | `SearchService` (Core) + `/search` |
| ☑ | Paginazione elenchi | `Helpers/Pager.cs`, `PostPager` | `?page=N` + `pager.liquid`, link rel prev/next |
| ☑ | Rating dei post | `RatingHandler` (`rating.axd`), `EnableRating` | `POST /rating` (un voto per visitatore, cookie) + stelle di `blog.js` |
| ✖ | Esempio di home page personalizzata | `FrontPageExample.aspx` | Era solo una pagina di esempio; la home personalizzata si ottiene con una pagina "home" o con un tema |

## 2. Commenti

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Commenti annidati, moderazione, approvazione/rifiuto, spam | `Comment.cs`, `CommentsController`, `CommentList.ascx` | Core + API + `POST /comment` (fetch con token antifalsificazione al posto del callback Web Forms) e comandi rapidi di moderazione dalla pagina |
| ☑ | Chiusura commenti dopo N giorni, sito web e nazione opzionali, anteprima, notifica e cancellazione dalle notifiche | `BlogSettings`, `post.aspx` | Core + temi (`?unsubscribe-email=`) |
| ☑ | Filtri commenti configurabili (IP, email, parole) | `CommentFilterRepository`, `CommentFilterController` | Core + API |
| ☑ | Filtri antispam personalizzati (`ICustomFilter`), segnalazione errori | `Services/Messaging/Protection` | `ICommentSpamFilter` registrato dalle estensioni (Akismet, StopForumSpam) |
| ☑ | Avatar (Gravatar, ecc.) | `Data/Services/Avatar.cs` | `AvatarService` + `comment.avatar` |
| ☑ | Provider commenti alternativi: Disqus, Facebook | `CommentProvider`, `Disqus*`, `Facebook*` | `post.liquid`, `page.liquid` (Disqus anche sulle pagine) |
| ✖ | CoComment | `IsCoCommentEnabled` | Servizio esterno chiuso da anni |

## 3. Utenti, ruoli e sicurezza

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Login, logout, cambio password, recupero password | `Account/*.aspx`, `Logout.cshtml` | Razor Pages + autenticazione via cookie |
| ☑ | Registrazione autonoma con ruolo iniziale | `register.aspx`, `EnableSelfRegistration` | Razor Pages |
| ✖ | Creazione di un blog alla registrazione | `create-blog.aspx`, `CreateBlogOnSelfRegistration` | Sostituita dal permesso "CreateNewBlogs": creare blog figli spetta agli amministratori, che possono concederlo ad altri ruoli (Utenti → Ruoli); modifica ed eliminazione restano agli amministratori |
| ☑ | Utenti e ruoli (store XML / SQLite) | `XmlMembershipProvider`, `XmlRoleProvider` | Store personalizzato + `PasswordHasher` |
| ☑ | Permessi granulari (`Rights`: ~40 diritti, 8 gruppi) | `Services/Security/Rights.cs`, `Right.cs` | Core + policy di autorizzazione |
| ◐ | Profili autore | `AuthorProfile.cs`, `Profiles.cs` | Core + API |
| ☑ | Gestione www / non-www | `WwwSubDomainModule`, `HandleWwwSubdomain` | `WwwRedirectMiddleware` (redirect permanente) |
| ☑ | SSL obbligatorio per MetaWeblog | `RequireSslMetaWeblogApi` | `MetaWeblogController` |
| ☑ | Persistenza chiavi di cifratura su hosting condiviso | (nuovo) | `App_Data/keys` (Fase 1) |

## 4. Multi-blog

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Più blog nella stessa applicazione (sottocartella o host) | `Blog.cs`, `BlogsController`, `App_Data/blogs` | Core + middleware di risoluzione del blog |
| ✖ | Blog principale che vede i contenuti dei blog figli | `Blog.cs` | Scelta: blog principale e figli restano distinti, ognuno mostra solo i propri contenuti; dal principale si passa ai figli con il widget Blog List. Opzione IsSiteAggregation rimossa |
| ☑ | Generatore di nuovi blog da modello | `Helpers/BlogGenerator.cs` | Core |

## 5. Amministrazione (AngularJS 1.3, mantenuto nella prima fase)

| Stato | Sezione | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Dashboard e statistiche | `dashboard/`, `DashboardController`, `StatsController` | Contratti JSON invariati |
| ☑ | Editor di post e pagine (TinyMCE, Summernote, bootstrap-wysiwyg) | `admin/app/editor`, `admin/editors` | Le pagine `.cshtml` dell'editor diventano Razor ASP.NET Core |
| ◐ | File manager, upload | `FileManagerController`, `UploadController` | File in `App_Data/files` |
| ☑ | Contenuti: post, pagine, categorie, tag, commenti, blog | `admin/app/content/*` | API |
| ☑ | Utenti, ruoli, profilo | `admin/app/security/*` | API |
| ☑ | Impostazioni: base, avanzate, commenti, email, feed, controlli | `admin/app/settings/*`, `SettingsController` | API |
| ☑ | Blogroll, ping service | `settings/controls`, `BlogRollController`, `PingServicesController` | API |
| ☑ | Strumenti di verifica (email, permessi cartelle) | `settings/tools`, `ToolsController` | API, integrati con la diagnostica |
| ☑ | Log | `LogsController`, `UnhandledExceptionLogger` | `App_Data/logs` |
| ☑ | Gestione temi, widget, estensioni (plugin) | `admin/app/custom/*`, `PackagesController`, `PackageExtraController` | Installazione copiando la cartella; attivazione, priorità, rimozione di quelle caricate in `App_Data` |
| ☑ | Impostazioni delle estensioni | `admin/Extensions/Settings.aspx` | Razor Page `/admin/extensions/settings` generata dalle impostazioni dichiarate |
| ☑ | Lookup (culture, fusi orari, editor…) | `LookupsController` | API |
| ☑ | News feed BlogEngine in dashboard | `NewsFeedController` | Da verificare: la fonte esterna potrebbe non esistere più |
| ✖ | Galleria online di temi ed estensioni | `Services/Packaging/Gallery*`, NuGet.Core | Servizio online non più disponibile |

## 6. Temi e widget

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Motore dei temi installabili da cartella | `.master` + `.ascx` in `Custom/Themes` | Fluid: `Custom/Themes` e `App_Data/themes`, riletti a ogni modifica; template predefiniti in `Templates/`; anteprima con `?theme=`, cookie del tema; guida in `Custom/Themes/README.md` |
| ☑ | Tema `Standard` | `Custom/Themes/Standard` | Convertito in Liquid |
| ☑ | Tema `Standard-2017` | `Custom/Themes/Standard-2017` | Convertito in Liquid |
| ✖ | Tema `RazorHost` | `Custom/Themes/RazorHost` | Non era un tema grafico ma il ponte per scrivere temi in Razor dentro Web Forms: il motore Liquid ne prende il posto |
| ☑ | Tema dell'amministrazione | `admin/themes/standard` | Statico (Fase 4b) |
| ◐ | Tema mobile alternativo | `MobileThemeSwitch.cs` | I temi inclusi sono responsive; il cookie `ThemeCookieName` permette comunque di scegliere un altro tema (es. da un'estensione, Fase 6) |
| ☑ | Zone dei widget, ordinamento, configurazione | `WidgetZone`, `WidgetContainer`, `WidgetsController` | Tag `widget_zone`, `WidgetService`, API `/api/widgets`; zone lette dai template, widget predefiniti in `theme.json` |
| ☑ | Widget installabili da cartella (`widget.xml` + vista + edit) | `Custom/Widgets/*` | `widget.liquid` + `widget.json`; pagina di modifica generata dalle impostazioni del manifest; guida in `Custom/Widgets/README.md` |
| ☑ | Widget inclusi: Administration, AuthorList, BlogList, BlogRoll, CategoryList, CommentList, LinkList, MonthList, Newsletter, PageList, PostList, Search, TagCloud, TextBox | `Custom/Widgets` | Convertiti (BlogRoll scarica i feed in background) |
| ☑ | Menu delle pagine, casella di ricerca | `PageMenu`, `SearchBox`, `SearchOnSearch` | `blog.pages` (con sottopagine), widget Search |
| ☑ | Header HTML personalizzato, script di tracking | `HtmlHeader`, `TrackingScript` | `content_for_header`, `content_for_footer` |
| ☑ | Titoli con nome del blog | `UseBlogNameInPageTitles` | `page_title` |

## 7. Estensioni

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Gestore estensioni: attivazione, priorità, impostazioni | `Web/Extensions/ExtensionManager.cs`, `ExtensionSettings` | `ExtensionRegistry`, `ExtensionStates` (stato valido per tutti i blog, senza riavvio), `IExtensionSettingsService`; API `/api/packages` |
| ☑ | Estensioni in sorgente `.cs` compilate a runtime | (nuovo: oggi compilate col sito) | Roslyn all'avvio, cache in `App_Data/cache/extensions`, riavvio automatico quando i file cambiano; guida in `Custom/Extensions/README.md` |
| ☑ | Estensioni in DLL precompilate | (nuovo) | `.dll` nella cartella dell'estensione, caricate in un `AssemblyLoadContext` proprio |
| ☑ | Eventi del ciclo di vita su post, pagine, commenti, categorie, ricerca, email, referrer… | eventi statici (`Serving`, `Saving`, `Saved`, `AddingComment`, `CommentAdded`, `Publishing`, `Searching`, `EmailSent`, `SpamAttack`, …) | `IEventHandler<T>` registrati dalle estensioni; quelli delle estensioni disattivate vengono saltati |
| ☑ | AkismetFilter | `Custom/Extensions/AkismetFilter.cs` | Convertita (disattivata finché non si inserisce la chiave) |
| ☑ | BBCode | `BBCode.cs` | Convertita |
| ☑ | BreakPost | `BreakPost.cs` | Convertita |
| ✖ | Logger | `Logger.cs` | Il log su file è sempre attivo (`App_Data/logs`), non serve più un'estensione |
| ☑ | MediaElementPlayer | `MediaElementPlayer.cs` | Convertita; video caricati dall'editor nella cartella `media` del blog, serviti da `/media/...` con riproduzione a salti |
| ☑ | Recaptcha | `Recaptcha/` | Portata a reCAPTCHA v2 (la v1 non esiste più); commenti e, se attivato, contatti. Il visualizzatore del log dei tentativi è stato tolto |
| ☑ | ResolveLinks | `ResolveLinks.cs` | Convertita |
| ☑ | SendCommentMail | `SendCommentMail.cs` | Convertita (email in background) |
| ☑ | SendNewsletters | `SendNewsletters.cs` | Convertita (usa `newsletter.html` del tema) |
| ☑ | SendPings | `SendPings.cs` | Ping ai servizi XML-RPC e invio di pingback/trackback ai siti citati (`LinkbackSender`) |
| ☑ | SimpleCaptcha | `SimpleCaptcha/` | Convertita (attiva per default, come in origine) |
| ☑ | Smilies | `Smilies.cs` | Convertita |
| ☑ | StopForumSpam | `StopForumSpam.cs` | Convertita (API JSON attuale) |
| ☑ | Datastore delle impostazioni delle estensioni | `Web/Extensions/DataStore`, `XmlProvider/DataStore.cs` | Archivio delle estensioni dei provider XML e SQLite; un blog senza impostazioni proprie usa quelle del principale |

## 8. Endpoint pubblici e protocolli (URL `*.axd` invariati)

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Feed RSS 2.0 / Atom, per categoria, tag, autore, commenti | `SyndicationHandler` (`syndication.axd`), `SyndicationGenerator` | `FeedController`: stessi parametri e estensioni (dc, slash, wfw, pingback, trackback, betag), anche `category/feed/`, `tag/feed/`, `post/feed/`, risposta 304 |
| ☑ | Feed alternativo (es. FeedBurner), numero voci | `AlternateFeedUrl`, `PostsPerFeed` | Link del feed nelle pagine e `maxitems` |
| ☑ | Sitemap XML | `SiteMap` (`sitemap.axd`) | `SemanticController` |
| ☑ | MetaWeblog / XML-RPC (Windows Live Writer, Open Live Writer) | `API/MetaWeblog` (`metaweblog.axd`), `wlwmanifest.xml`, `AppCode/Wlw` | `MetaWeblogController`: metaWeblog.*, blogger.*, wp.* (pagine, autori, tag), immagini; tentativi limitati. Tolto il pulsante "Tags" di Live Writer (TagMiniView) |
| ☑ | RSD | `RsdHandler` (`rsd.axd`) | `SemanticController` |
| ☑ | Pingback e trackback, in ricezione e invio | `PingbackHandler`, `TrackbackHandler`, `Services/Messaging/Ping` | `LinkbackController` (verifica della pagina d'origine), `LinkbackSender`; le pagine esterne si scaricano solo da indirizzi pubblici |
| ☑ | OpenSearch | `OpenSearchHandler` (`opensearch.axd`) | `SemanticController` |
| ☑ | OPML (blogroll) | `OpmlHandler` (`opml.axd`) | `SemanticController` |
| ☑ | APML, FOAF, SIOC | `Apml`, `Foaf`, `Sioc` | `SemanticController` (`foaf.axd`, `foaf_nome.axd`, `sioc.axd`, `apml.axd`); il filtro dei feed tramite APML remoto è stato tolto |
| ☑ | Export BlogML | `BlogMLExportHandler` (`blogml.axd`) | `BlogMLService` (solo amministratori) |
| ☑ | Import BlogML | `Services/Syndication/BlogML` | Impostazioni avanzate → Importa; tolto l'importatore ClickOnce (servizio SOAP non più esistente) |
| ☑ | File e immagini serviti con controllo accessi, ridimensionamento | `FileHandler` (`file.axd`), `ImageHandler` (`image.axd`) | `file.axd`, `image.axd`, `/media/`. Il ridimensionamento non è stato portato: in origine lo usava solo il provider database (non più supportato) e nessuna pagina lo esponeva |
| ☑ | Risorse localizzate per JavaScript | `ResourceHandler` (`*.res.axd`) | `admin.res.axd` per l'amministrazione, `BlogEngineRes` nelle pagine pubbliche |
| ✖ | JavaScript minificato | `JavaScriptHandler` (`*.js.axd`), AjaxMin | Gli script inclusi sono già minificati e serviti come file statici |
| ☑ | `robots.txt` | file statico | Generato, con l'indirizzo della sitemap |
| ☑ | Download di file remoti da parte del server | `RemoteFile.cs`, `AllowServerToDownloadRemoteFiles` | `RemotePages` (solo indirizzi pubblici, dimensione e tempo limitati); usato dall'importazione BlogML per le immagini |

## 9. URL e routing

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Permalink di post (con o senza data), pagine, categorie, tag, autori, calendario, contatti, archivio, ricerca | `UrlRewrite` + `UrlRules.cs` | `BlogController`; le varianti di un post rimandano all'indirizzo definitivo |
| ☑ | URL senza `.aspx` e redirect da quelli vecchi | `RedirectToRemoveFileExtension` | `LegacyUrlMiddleware`: redirect permanenti (post, pagine, default, archivio, ricerca, account, post.aspx?id=, page.aspx?id=) |
| ☑ | Pagine di errore e 404 personalizzate | `error.aspx`, `error404.aspx`, `ErrorTitle`, `ErrorText` | `/error/{codice}` con il tema e suggerimenti di contenuti simili |
| ☑ | Tracciamento referrer | `ReferrerModule`, `Referrer.cs` | `ReferrerMiddleware` (se attivato nelle impostazioni) |

## 10. Email, contatti, newsletter

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ◐ | Invio email SMTP (SSL, prefisso oggetto) | `Utils.SendMailMessage`, `BlogSettings` | Core (MailKit o `SmtpClient`) |
| ☑ | Form contatti con allegati e reCAPTCHA | `contact.aspx` | `/contact` con allegati, antispam (campo nascosto + limite di invii) e captcha delle estensioni |
| ☑ | Notifica email dei commenti | estensione SendCommentMail, `Post.SendNotifications` | Al proprietario con SendCommentMail; agli iscritti dal Core (`CommentNotificationHandler`), con link per cancellarsi |
| ☑ | Newsletter | widget Newsletter + estensione SendNewsletters | Iscrizione dal widget, gestione degli iscritti, invio alla pubblicazione |

## 11. Localizzazione e impostazioni generali

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ◐ | Interfaccia in 37 lingue | `App_GlobalResources/labels.*.resx` | Resx in Core + `IStringLocalizer` |
| ◐ | Cultura e fuso orario del blog | `Culture`, `TimeZoneId`, `BlogCulture.cs` | Middleware di localizzazione |
| ☑ | Calendario persiano | `AppCode/PersianCulture.cs` | Con .NET 8 (ICU) la cultura fa-IR usa già il calendario persiano: la classe di correzione non serve più |
| ◐ | ~90 impostazioni del blog | `BlogSettings.cs` | Core, stessi nomi |
| ☑ | Compressione delle risposte | `CompressionModule`, `CompressWebResource` | gzip/brotli con `EnableHttpCompression` |
| ✖ | Bundling/minificazione | `EnableOptimization`, System.Web.Optimization | Non necessario: file già minificati, compressione e cache del browser |
| ◐ | Cache | `Providers/CacheProvider` | `IMemoryCache` |

## 12. Dati e installazione

| Stato | Funzionalità | Oggi | Destinazione / note |
|---|---|---|---|
| ☑ | Provider XML | `Providers/XmlProvider` | `BlogEngine.Data.Xml` |
| ☑ | Provider SQLite | `setup/SQLite` + `DbBlogProvider` | `BlogEngine.Data.Sqlite` (EF Core) |
| ☐ | File system dei file caricati | `XmlFileSystemProvider` | Disco, in `App_Data/files` |
| ☑ | Configurazione iniziale via browser (provider, admin) | `SetupController`, `setup/` | Razor Page |
| ☑ | Pagina di diagnostica (permessi, versione runtime, percorsi) | (nuovo) | `/admin/diagnostics` (link dalla pagina "Informazioni") |
| ☑ | Percorso dati configurabile, cartelle create all'avvio | (nuovo) | `BlogEnginePaths` (Fase 1) |
| ✖ | Provider SQL Server, MySQL, SQL CE | `setup/SQLServer`, `MySQL`, `SQL_CE`, `DbMembershipProvider` | Non richiesti |
| ✖ | File system su database o UNC | `DbFileSystemProvider`, `UNCFileSystemProvider`, LINQ to SQL | Non richiesti |
| ✖ | Script di upgrade e supporto Mono | `setup/upgrade`, `setup/Mono` | Installazione nuova; .NET 8 è già multipiattaforma |
| ✖ | Expression Builder di Web Forms | `Services/Compilation` | Specifici di Web Forms |
