// Generato dal vecchio Data/Models/Settings.cs e Data/ViewModels/SettingsVM.cs: stessi nomi e stessi elenchi.

namespace BlogEngine.Web.Api.Models;

/// <summary>Impostazioni del blog come le scambia l'amministrazione ("Settings" nella versione originale).</summary>
public sealed class SettingsDto
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool EnableMaintenanceMode { get; set; }

    public string? MaintenanceMessage { get; set; }

    public int PostsPerPage { get; set; }

    public string? ThemeCookieName { get; set; }

    public bool UseBlogNameInPageTitles { get; set; }

    public bool EnableRelatedPosts { get; set; }

    public bool EnableRating { get; set; }

    public bool ShowDescriptionInPostList { get; set; }

    public int DescriptionCharacters { get; set; }

    public bool ShowDescriptionInPostListForPostsByTagOrCategory { get; set; }

    public int DescriptionCharactersForPostsByTagOrCategory { get; set; }

    public bool TimeStampPostLinks { get; set; }

    public bool ShowPostNavigation { get; set; }

    public string? Culture { get; set; }

    public string? TimeZoneId { get; set; }

    public bool RemoveExtensionsFromUrls { get; set; }

    public bool RedirectToRemoveFileExtension { get; set; }

    public string? HandleWwwSubdomain { get; set; }

    public string? DesktopTheme { get; set; }

    public bool EnableHttpCompression { get; set; }

    public bool CompressWebResource { get; set; }

    public bool EnableOpenSearch { get; set; }

    public bool RequireSslForMetaWeblogApi { get; set; }

    public bool EnablePasswordReset { get; set; }

    public bool EnableSelfRegistration { get; set; }


    public bool AllowServerToDownloadRemoteFiles { get; set; }

    public int RemoteFileDownloadTimeout { get; set; }

    public int RemoteMaxFileSize { get; set; }

    public string? SelfRegistrationInitialRole { get; set; }

    public string? AuthorName { get; set; }

    public string? FeedAuthor { get; set; }

    public string? Endorsement { get; set; }

    public string? AlternateFeedUrl { get; set; }

    public string? Language { get; set; }

    public int PostsPerFeed { get; set; }

    public bool EnableEnclosures { get; set; }

    public bool EnableTagExport { get; set; }

    public string? SyndicationFormat { get; set; }

    public string? Email { get; set; }

    public string? SmtpServer { get; set; }

    public int SmtpServerPort { get; set; }

    public string? SmtpUserName { get; set; }

    public string? SmtpPassword { get; set; }

    public string? EmailSubjectPrefix { get; set; }

    public bool EnableSsl { get; set; }

    public bool SendMailOnComment { get; set; }

    public string? SearchButtonText { get; set; }

    public string? SearchCommentLabelText { get; set; }

    public string? SearchDefaultText { get; set; }

    public bool EnableCommentSearch { get; set; }

    public bool ShowIncludeCommentsOption { get; set; }

    public string? ContactFormMessage { get; set; }

    public string? ContactThankMessage { get; set; }

    public string? ContactErrorMessage { get; set; }

    public bool EnableContactAttachments { get; set; }

    public bool EnableRecaptchaOnContactForm { get; set; }

    public string? ErrorTitle { get; set; }

    public string? ErrorText { get; set; }

    public string? HtmlHeader { get; set; }

    public string? TrackingScript { get; set; }

    public bool EnableCookieConsent { get; set; }

    public string? CookiePolicyUrl { get; set; }

    public int DaysCommentsAreEnabled { get; set; }

    public bool IsCommentsEnabled { get; set; }

    public bool EnableCommentsModeration { get; set; }

    public bool IsCommentNestingEnabled { get; set; }

    public string? Avatar { get; set; }

    public bool EnablePingBackSend { get; set; }

    public bool EnablePingBackReceive { get; set; }

    public bool EnableTrackBackSend { get; set; }

    public bool EnableTrackBackReceive { get; set; }

    public int CommentsPerPage { get; set; }

    public bool EnableCountryInComments { get; set; }

    public bool EnableWebsiteInComments { get; set; }

    public bool ShowLivePreview { get; set; }

    public BlogEngine.Settings.CommentProvider CommentProvider { get; set; }

    public bool DisqusDevMode { get; set; }

    public bool DisqusAddCommentsToPages { get; set; }

    public string? DisqusWebsiteName { get; set; }

    public string? FacebookAppId { get; set; }

    public string? FacebookLanguage { get; set; }

    public bool TrustAuthenticatedUsers { get; set; }
}

public sealed class SettingsVM
{
    public SettingsDto Settings { get; set; } = new();

    public List<SelectOption> TimeZones { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => new SelectOption { OptionName = z.DisplayName, OptionValue = z.Id })
        .ToList();

    public List<SelectOption> FeedOptions { get; } =
    [
        new() { OptionName = "RSS 2.0", OptionValue = "Rss" },
        new() { OptionName = "Atom 1.0", OptionValue = "Atom" },
    ];

    public List<SelectOption> CommentsPerPageOptions { get; } =
    [
        new() { OptionName = "5", OptionValue = "5" },
        new() { OptionName = "10", OptionValue = "10" },
        new() { OptionName = "15", OptionValue = "15" },
        new() { OptionName = "20", OptionValue = "20" },
        new() { OptionName = "50", OptionValue = "50" },
    ];

    public List<SelectOption> FacebookLanguages { get; } =
    [
        new() { OptionName = "Afrikaans", OptionValue = "af_ZA" },
        new() { OptionName = "Akan", OptionValue = "ak_GH" },
        new() { OptionName = "Amharic", OptionValue = "am_ET" },
        new() { OptionName = "Arabic", OptionValue = "ar_AR" },
        new() { OptionName = "Assamese", OptionValue = "as_IN" },
        new() { OptionName = "Aymara", OptionValue = "ay_BO" },
        new() { OptionName = "Azerbaijani", OptionValue = "az_AZ" },
        new() { OptionName = "Belarusian", OptionValue = "be_BY" },
        new() { OptionName = "Bulgarian", OptionValue = "bg_BG" },
        new() { OptionName = "Bengali", OptionValue = "bn_IN" },
        new() { OptionName = "Breton", OptionValue = "br_FR" },
        new() { OptionName = "Bosnian", OptionValue = "bs_BA" },
        new() { OptionName = "Catalan", OptionValue = "ca_ES" },
        new() { OptionName = "SoraniKurdish", OptionValue = "cb_IQ" },
        new() { OptionName = "Cherokee", OptionValue = "ck_US" },
        new() { OptionName = "Corsican", OptionValue = "co_FR" },
        new() { OptionName = "Czech", OptionValue = "cs_CZ" },
        new() { OptionName = "Cebuano", OptionValue = "cx_PH" },
        new() { OptionName = "Welsh", OptionValue = "cy_GB" },
        new() { OptionName = "Danish", OptionValue = "da_DK" },
        new() { OptionName = "German", OptionValue = "de_DE" },
        new() { OptionName = "Greek", OptionValue = "el_GR" },
        new() { OptionName = "English(UK)", OptionValue = "en_GB" },
        new() { OptionName = "English(India)", OptionValue = "en_IN" },
        new() { OptionName = "English(Pirate)", OptionValue = "en_PI" },
        new() { OptionName = "English(UpsideDown)", OptionValue = "en_UD" },
        new() { OptionName = "English(US)", OptionValue = "en_US" },
        new() { OptionName = "Esperanto", OptionValue = "eo_EO" },
        new() { OptionName = "Spanish(Chile)", OptionValue = "es_CL" },
        new() { OptionName = "Spanish(Colombia)", OptionValue = "es_CO" },
        new() { OptionName = "Spanish(Spain)", OptionValue = "es_ES" },
        new() { OptionName = "Spanish", OptionValue = "es_LA" },
        new() { OptionName = "Spanish(Mexico)", OptionValue = "es_MX" },
        new() { OptionName = "Spanish(Venezuela)", OptionValue = "es_VE" },
        new() { OptionName = "Estonian", OptionValue = "et_EE" },
        new() { OptionName = "Basque", OptionValue = "eu_ES" },
        new() { OptionName = "Persian", OptionValue = "fa_IR" },
        new() { OptionName = "LeetSpeak", OptionValue = "fb_LT" },
        new() { OptionName = "Fulah", OptionValue = "ff_NG" },
        new() { OptionName = "Finnish", OptionValue = "fi_FI" },
        new() { OptionName = "Faroese", OptionValue = "fo_FO" },
        new() { OptionName = "French(Canada)", OptionValue = "fr_CA" },
        new() { OptionName = "French(France)", OptionValue = "fr_FR" },
        new() { OptionName = "Frisian", OptionValue = "fy_NL" },
        new() { OptionName = "Irish", OptionValue = "ga_IE" },
        new() { OptionName = "Galician", OptionValue = "gl_ES" },
        new() { OptionName = "Guarani", OptionValue = "gn_PY" },
        new() { OptionName = "Gujarati", OptionValue = "gu_IN" },
        new() { OptionName = "ClassicalGreek", OptionValue = "gx_GR" },
        new() { OptionName = "Hausa", OptionValue = "ha_NG" },
        new() { OptionName = "Hebrew", OptionValue = "he_IL" },
        new() { OptionName = "Hindi", OptionValue = "hi_IN" },
        new() { OptionName = "Croatian", OptionValue = "hr_HR" },
        new() { OptionName = "Hungarian", OptionValue = "hu_HU" },
        new() { OptionName = "Armenian", OptionValue = "hy_AM" },
        new() { OptionName = "Indonesian", OptionValue = "id_ID" },
        new() { OptionName = "Igbo", OptionValue = "ig_NG" },
        new() { OptionName = "Icelandic", OptionValue = "is_IS" },
        new() { OptionName = "Italian", OptionValue = "it_IT" },
        new() { OptionName = "Japanese", OptionValue = "ja_JP" },
        new() { OptionName = "Japanese(Kansai)", OptionValue = "ja_KS" },
        new() { OptionName = "Javanese", OptionValue = "jv_ID" },
        new() { OptionName = "Georgian", OptionValue = "ka_GE" },
        new() { OptionName = "Kazakh", OptionValue = "kk_KZ" },
        new() { OptionName = "Khmer", OptionValue = "km_KH" },
        new() { OptionName = "Kannada", OptionValue = "kn_IN" },
        new() { OptionName = "Korean", OptionValue = "ko_KR" },
        new() { OptionName = "Kurdish(Kurmanji)", OptionValue = "ku_TR" },
        new() { OptionName = "Latin", OptionValue = "la_VA" },
        new() { OptionName = "Ganda", OptionValue = "lg_UG" },
        new() { OptionName = "Limburgish", OptionValue = "li_NL" },
        new() { OptionName = "Lingala", OptionValue = "ln_CD" },
        new() { OptionName = "Lao", OptionValue = "lo_LA" },
        new() { OptionName = "Lithuanian", OptionValue = "lt_LT" },
        new() { OptionName = "Latvian", OptionValue = "lv_LV" },
        new() { OptionName = "Malagasy", OptionValue = "mg_MG" },
        new() { OptionName = "Macedonian", OptionValue = "mk_MK" },
        new() { OptionName = "Malayalam", OptionValue = "ml_IN" },
        new() { OptionName = "Mongolian", OptionValue = "mn_MN" },
        new() { OptionName = "Marathi", OptionValue = "mr_IN" },
        new() { OptionName = "Malay", OptionValue = "ms_MY" },
        new() { OptionName = "Maltese", OptionValue = "mt_MT" },
        new() { OptionName = "Burmese", OptionValue = "my_MM" },
        new() { OptionName = "Norwegian(bokmal)", OptionValue = "nb_NO" },
        new() { OptionName = "Ndebele", OptionValue = "nd_ZW" },
        new() { OptionName = "Nepali", OptionValue = "ne_NP" },
        new() { OptionName = "Dutch(België)", OptionValue = "nl_BE" },
        new() { OptionName = "Dutch", OptionValue = "nl_NL" },
        new() { OptionName = "Norwegian(nynorsk)", OptionValue = "nn_NO" },
        new() { OptionName = "Chewa", OptionValue = "ny_MW" },
        new() { OptionName = "Oriya", OptionValue = "or_IN" },
        new() { OptionName = "Punjabi", OptionValue = "pa_IN" },
        new() { OptionName = "Polish", OptionValue = "pl_PL" },
        new() { OptionName = "Pashto", OptionValue = "ps_AF" },
        new() { OptionName = "Portuguese(Brazil)", OptionValue = "pt_BR" },
        new() { OptionName = "Portuguese(Portugal)", OptionValue = "pt_PT" },
        new() { OptionName = "Quechua", OptionValue = "qu_PE" },
        new() { OptionName = "Romansh", OptionValue = "rm_CH" },
        new() { OptionName = "Romanian", OptionValue = "ro_RO" },
        new() { OptionName = "Russian", OptionValue = "ru_RU" },
        new() { OptionName = "Kinyarwanda", OptionValue = "rw_RW" },
        new() { OptionName = "Sanskrit", OptionValue = "sa_IN" },
        new() { OptionName = "Sardinian", OptionValue = "sc_IT" },
        new() { OptionName = "NorthernSámi", OptionValue = "se_NO" },
        new() { OptionName = "Sinhala", OptionValue = "si_LK" },
        new() { OptionName = "Slovak", OptionValue = "sk_SK" },
        new() { OptionName = "Slovenian", OptionValue = "sl_SI" },
        new() { OptionName = "Shona", OptionValue = "sn_ZW" },
        new() { OptionName = "Somali", OptionValue = "so_SO" },
        new() { OptionName = "Albanian", OptionValue = "sq_AL" },
        new() { OptionName = "Serbian", OptionValue = "sr_RS" },
        new() { OptionName = "Swedish", OptionValue = "sv_SE" },
        new() { OptionName = "Swahili", OptionValue = "sw_KE" },
        new() { OptionName = "Syriac", OptionValue = "sy_SY" },
        new() { OptionName = "Silesian", OptionValue = "sz_PL" },
        new() { OptionName = "Tamil", OptionValue = "ta_IN" },
        new() { OptionName = "Telugu", OptionValue = "te_IN" },
        new() { OptionName = "Tajik", OptionValue = "tg_TJ" },
        new() { OptionName = "Thai", OptionValue = "th_TH" },
        new() { OptionName = "Turkmen", OptionValue = "tk_TM" },
        new() { OptionName = "Filipino", OptionValue = "tl_PH" },
        new() { OptionName = "Klingon", OptionValue = "tl_ST" },
        new() { OptionName = "Turkish", OptionValue = "tr_TR" },
        new() { OptionName = "Tatar", OptionValue = "tt_RU" },
        new() { OptionName = "Tamazight", OptionValue = "tz_MA" },
        new() { OptionName = "Ukrainian", OptionValue = "uk_UA" },
        new() { OptionName = "Urdu", OptionValue = "ur_PK" },
        new() { OptionName = "Uzbek", OptionValue = "uz_UZ" },
        new() { OptionName = "Vietnamese", OptionValue = "vi_VN" },
        new() { OptionName = "Wolof", OptionValue = "wo_SN" },
        new() { OptionName = "Xhosa", OptionValue = "xh_ZA" },
        new() { OptionName = "Yiddish", OptionValue = "yi_DE" },
        new() { OptionName = "Yoruba", OptionValue = "yo_NG" },
        new() { OptionName = "SimplifiedChinese(China)", OptionValue = "zh_CN" },
        new() { OptionName = "TraditionalChinese(HongKong)", OptionValue = "zh_HK" },
        new() { OptionName = "TraditionalChinese(Taiwan)", OptionValue = "zh_TW" },
        new() { OptionName = "Zulu", OptionValue = "zu_ZA" },
        new() { OptionName = "Zazaki", OptionValue = "zz_TR" },
    ];

    public List<SelectOption> CloseDaysOptions { get; } =
    [
        new() { OptionName = "Never", OptionValue = "0" },
        new() { OptionName = "1", OptionValue = "1" },
        new() { OptionName = "2", OptionValue = "2" },
        new() { OptionName = "3", OptionValue = "3" },
        new() { OptionName = "7", OptionValue = "7" },
        new() { OptionName = "10", OptionValue = "10" },
        new() { OptionName = "14", OptionValue = "14" },
        new() { OptionName = "21", OptionValue = "21" },
        new() { OptionName = "30", OptionValue = "30" },
        new() { OptionName = "60", OptionValue = "60" },
        new() { OptionName = "90", OptionValue = "90" },
        new() { OptionName = "180", OptionValue = "180" },
        new() { OptionName = "365", OptionValue = "365" },
    ];
}
