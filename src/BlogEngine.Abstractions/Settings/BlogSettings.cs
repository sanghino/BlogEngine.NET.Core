namespace BlogEngine.Settings;

/// <summary>
/// Impostazioni di un blog. I valori predefiniti corrispondono a quelli di una nuova installazione.
/// Ogni proprietà viene memorizzata come coppia nome/valore: rinominarla equivale a perderne il valore.
/// </summary>
/// <remarks>
/// Rispetto alla versione .NET Framework sono state rimosse: IsCoCommentEnabled (servizio chiuso),
/// IsRazorTheme (i temi sono in Liquid), EnableOptimization e CompressWebResource (bundling e
/// minificazione avvengono in fase di build), RemoveExtensionsFromUrls (gli URL non hanno più estensione),
/// SecurityValidationKey (sostituita da ASP.NET Core Data Protection) e ModerationType (era fisso su Auto).
/// </remarks>
public sealed class BlogSettings
{
    // ---- Generali ----

    public string Name { get; set; } = "Name of the blog";

    public string Description { get; set; } = "Short description of the blog";

    public string AuthorName { get; set; } = "My name";

    public string Theme { get; set; } = "Standard";

    /// <summary>Cookie con cui un visitatore può scegliere un tema diverso (es. tema mobile).</summary>
    public string ThemeCookieName { get; set; } = "theme";

    /// <summary>Lingua dei contenuti del blog (per i feed).</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Cultura dell'interfaccia; "Auto" usa quella del browser.</summary>
    public string Culture { get; set; } = "Auto";

    /// <summary>Fuso orario del blog: id Windows o IANA.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public int PostsPerPage { get; set; } = 10;

    public bool ShowDescriptionInPostList { get; set; }

    public int DescriptionCharacters { get; set; } = 300;

    public bool ShowDescriptionInPostListForPostsByTagOrCategory { get; set; }

    public int DescriptionCharactersForPostsByTagOrCategory { get; set; }

    /// <summary>Inserisce la data (aaaa/mm/gg) nei link dei post.</summary>
    public bool TimeStampPostLinks { get; set; }

    public bool ShowPostNavigation { get; set; } = true;

    public bool EnableRelatedPosts { get; set; } = true;

    public bool EnableRating { get; set; } = true;

    public bool ShowLivePreview { get; set; }

    public bool UseBlogNameInPageTitles { get; set; } = true;

    public bool EnableEnclosures { get; set; }

    public bool EnableTagExport { get; set; } = true;

    public bool EnableOpenSearch { get; set; } = true;

    public bool EnableHttpCompression { get; set; }

    /// <summary>Gestione del prefisso "www": vuoto (nessuna azione), "add" o "remove".</summary>
    public string HandleWwwSubdomain { get; set; } = string.Empty;

    /// <summary>Reindirizza i vecchi URL con estensione ".aspx" a quelli senza estensione.</summary>
    public bool RedirectToRemoveFileExtension { get; set; }

    /// <summary>Codice HTML aggiunto nell'intestazione di ogni pagina.</summary>
    public string HtmlHeader { get; set; } = string.Empty;

    /// <summary>Script di statistiche aggiunto in fondo a ogni pagina.</summary>
    public string TrackingScript { get; set; } = string.Empty;

    public float GeocodingLatitude { get; set; }

    public float GeocodingLongitude { get; set; }

    /// <summary>Id della pagina da usare come home page al posto dell'elenco dei post.</summary>
    public string CustomFrontPage { get; set; } = string.Empty;

    // ---- Feed ----

    /// <summary>Formato dei feed: "Rss" o "Atom".</summary>
    public string SyndicationFormat { get; set; } = "Rss";

    public int PostsPerFeed { get; set; } = 3;

    public string FeedAuthor { get; set; } = string.Empty;

    /// <summary>Feed esterno (es. FeedBurner) verso cui reindirizzare i lettori.</summary>
    public string AlternateFeedUrl { get; set; } = string.Empty;

    public string Endorsement { get; set; } = "https://blogengine.io/syndication.axd";

    // ---- Commenti ----

    public bool IsCommentsEnabled { get; set; } = true;

    /// <summary>Giorni dopo la pubblicazione in cui i commenti restano aperti; 0 = sempre.</summary>
    public int DaysCommentsAreEnabled { get; set; }

    public bool EnableCommentsModeration { get; set; } = true;

    public bool IsCommentNestingEnabled { get; set; } = true;

    public int CommentsPerPage { get; set; } = 10;

    public bool EnableCountryInComments { get; set; }

    public bool EnableWebsiteInComments { get; set; } = true;

    /// <summary>Approva automaticamente i commenti degli utenti autenticati.</summary>
    public bool TrustAuthenticatedUsers { get; set; } = true;

    public bool CommentReportMistakes { get; set; }

    /// <summary>Servizio di avatar: "none", "gravatar", "identicon", ...</summary>
    public string Avatar { get; set; } = "none";

    public CommentProvider CommentProvider { get; set; } = CommentProvider.BlogEngine;

    public string DisqusWebsiteName { get; set; } = string.Empty;

    public bool DisqusDevMode { get; set; }

    public bool DisqusAddCommentsToPages { get; set; }

    public string FacebookAppId { get; set; } = string.Empty;

    public string FacebookLanguage { get; set; } = "en_US";

    // ---- Ping e trackback ----

    public bool EnablePingBackSend { get; set; } = true;

    public bool EnablePingBackReceive { get; set; } = true;

    public bool EnableTrackBackSend { get; set; } = true;

    public bool EnableTrackBackReceive { get; set; } = true;

    public bool RequireSslMetaWeblogApi { get; set; }

    // ---- Email ----

    public string Email { get; set; } = "youremail@gmail.com";

    public string SmtpServer { get; set; } = "smtp.gmail.com";

    public int SmtpServerPort { get; set; } = 587;

    public string SmtpUserName { get; set; } = "youremail@gmail.com";

    public string SmtpPassword { get; set; } = string.Empty;

    public bool EnableSsl { get; set; }

    public string EmailSubjectPrefix { get; set; } = "Weblog";

    public bool SendMailOnComment { get; set; } = true;

    // ---- Contatti ----

    public string ContactFormMessage { get; set; } = "<p>I'll answer the mail as soon as I can.</p>";

    public string ContactThankMessage { get; set; } = "<h1>Thank you</h1><p>The message was sent.</p>";

    public string ContactErrorMessage { get; set; } = "<p>This form does not work at the moment. Sorry for the inconvenience.</p>";

    public bool EnableContactAttachments { get; set; }

    public bool EnableRecaptchaOnContactForm { get; set; }

    // ---- Ricerca ----

    public bool EnableCommentSearch { get; set; } = true;

    public bool ShowIncludeCommentsOption { get; set; }

    public string SearchButtonText { get; set; } = "Search";

    public string SearchCommentLabelText { get; set; } = "Include comments in search";

    public string SearchDefaultText { get; set; } = "Enter search term or APML url";

    // ---- Blogroll e referrer ----

    public int BlogrollMaxLength { get; set; } = 23;

    public int BlogrollUpdateMinutes { get; set; } = 60;

    public int BlogrollVisiblePosts { get; set; } = 3;

    public bool EnableReferrerTracking { get; set; }

    public int NumberOfReferrerDays { get; set; } = 1;

    // ---- Registrazione utenti ----

    public bool EnableSelfRegistration { get; set; }

    public string SelfRegistrationInitialRole { get; set; } = "Editors";

    public bool EnablePasswordReset { get; set; } = true;

    // ---- File remoti ----

    public bool AllowServerToDownloadRemoteFiles { get; set; } = true;

    /// <summary>Timeout dei download remoti, in millisecondi.</summary>
    public int RemoteFileDownloadTimeout { get; set; } = 30000;

    /// <summary>Dimensione massima dei download remoti, in byte.</summary>
    public int RemoteMaxFileSize { get; set; } = 524288;

    // ---- Pagine di errore ----

    public string ErrorTitle { get; set; } = "Ooops! An unexpected error has occurred.";

    public string ErrorText { get; set; } = "This one's down to me! Please accept my apologies for this.";

    // ---- Opzioni dell'editor ----

    public bool PostOptionsSlug { get; set; } = true;

    public bool PostOptionsDescription { get; set; } = true;

    public bool PostOptionsCustomFields { get; set; }

    public bool PageOptionsSlug { get; set; } = true;

    public bool PageOptionsDescription { get; set; } = true;

    public bool PageOptionsCustomFields { get; set; } = true;

    /// <summary>Copia indipendente delle impostazioni.</summary>
    public BlogSettings Clone() => (BlogSettings)MemberwiseClone();
}

/// <summary>Sistema usato per i commenti.</summary>
public enum CommentProvider
{
    BlogEngine = 0,
    Disqus = 1,
    Facebook = 2,
}
