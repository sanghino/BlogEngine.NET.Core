using System.Net;
using System.Net.Mail;
using BlogEngine.Core.Caching;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/settings")]
public sealed class SettingsController(
    ISettingsService settings,
    IRightsService rights,
    IBlogContext context,
    IEnumerable<IBlogScopedCache> caches) : AdminApiController
{
    [HttpGet("")]
    public async Task<SettingsVM> Get()
    {
        await DemandAsync();
        return new SettingsVM { Settings = ToDto(await settings.GetAsync()) };
    }

    /// <summary>
    /// Salva le impostazioni. Con action=testEmail invia invece un messaggio di prova con i dati SMTP ricevuti
    /// (restituisce il testo dell'errore, vuoto se riuscito); con action=clearCache svuota la cache del blog.
    /// </summary>
    [HttpPut("")]
    public async Task<IActionResult> Put([FromBody] SettingsDto dto, string action = "")
    {
        await DemandAsync();
        switch (action)
        {
            case "testEmail":
                var error = await SendTestEmailAsync(dto);
                return string.IsNullOrEmpty(error) ? Ok() : Ok(error);
            case "clearCache":
                foreach (var cache in caches)
                {
                    cache.Invalidate(context.Blog.Id);
                }

                return Ok();
            default:
                var current = await settings.GetAsync();
                Apply(dto, current);
                await settings.SaveAsync(current);
                return Ok();
        }
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private async Task<string?> SendTestEmailAsync(SettingsDto dto)
    {
        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(dto.Email ?? string.Empty, dto.SmtpUserName),
                Subject = $"Test mail from {dto.SmtpUserName}",
                IsBodyHtml = true,
                Body = "<div style=\"font: 11px verdana, arial\">Success<br /><br />"
                    + $"<strong>IP address:</strong> {WebUtility.HtmlEncode(HttpContext.Connection.RemoteIpAddress?.ToString())}<br />"
                    + $"<strong>User-agent:</strong> {WebUtility.HtmlEncode(Request.Headers.UserAgent.ToString())}</div>",
            };
            mail.To.Add(mail.From);

            using var client = new SmtpClient(dto.SmtpServer, dto.SmtpServerPort) { EnableSsl = dto.EnableSsl };
            if (!string.IsNullOrWhiteSpace(dto.SmtpUserName))
            {
                client.Credentials = new NetworkCredential(dto.SmtpUserName, dto.SmtpPassword);
            }

            await client.SendMailAsync(mail, HttpContext.RequestAborted);
            return null;
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or ArgumentException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    private static SettingsDto ToDto(BlogSettings s) => new()
    {
        Name = s.Name,
        Description = s.Description,
        PostsPerPage = s.PostsPerPage,
        ThemeCookieName = s.ThemeCookieName,
        UseBlogNameInPageTitles = s.UseBlogNameInPageTitles,
        EnableRelatedPosts = s.EnableRelatedPosts,
        EnableRating = s.EnableRating,
        ShowDescriptionInPostList = s.ShowDescriptionInPostList,
        DescriptionCharacters = s.DescriptionCharacters,
        ShowDescriptionInPostListForPostsByTagOrCategory = s.ShowDescriptionInPostListForPostsByTagOrCategory,
        DescriptionCharactersForPostsByTagOrCategory = s.DescriptionCharactersForPostsByTagOrCategory,
        TimeStampPostLinks = s.TimeStampPostLinks,
        ShowPostNavigation = s.ShowPostNavigation,
        Culture = s.Culture,
        TimeZoneId = string.IsNullOrEmpty(s.TimeZoneId) ? "UTC" : s.TimeZoneId,
        RemoveExtensionsFromUrls = true,
        RedirectToRemoveFileExtension = s.RedirectToRemoveFileExtension,
        DesktopTheme = s.Theme,
        HandleWwwSubdomain = s.HandleWwwSubdomain,
        EnableHttpCompression = s.EnableHttpCompression,
        EnableOpenSearch = s.EnableOpenSearch,
        RequireSslForMetaWeblogApi = s.RequireSslMetaWeblogApi,
        EnablePasswordReset = s.EnablePasswordReset,
        EnableSelfRegistration = s.EnableSelfRegistration,
        AllowServerToDownloadRemoteFiles = s.AllowServerToDownloadRemoteFiles,
        RemoteFileDownloadTimeout = s.RemoteFileDownloadTimeout,
        RemoteMaxFileSize = s.RemoteMaxFileSize,
        SelfRegistrationInitialRole = string.IsNullOrEmpty(s.SelfRegistrationInitialRole) ? SystemRoles.Registered : s.SelfRegistrationInitialRole,
        AuthorName = s.AuthorName,
        FeedAuthor = s.FeedAuthor,
        Endorsement = s.Endorsement,
        AlternateFeedUrl = s.AlternateFeedUrl,
        Language = s.Language,
        PostsPerFeed = s.PostsPerFeed,
        EnableEnclosures = s.EnableEnclosures,
        SyndicationFormat = s.SyndicationFormat,
        EnableTagExport = s.EnableTagExport,
        Email = s.Email,
        SmtpServer = s.SmtpServer,
        SmtpServerPort = s.SmtpServerPort,
        SmtpUserName = s.SmtpUserName,
        SmtpPassword = s.SmtpPassword,
        EmailSubjectPrefix = s.EmailSubjectPrefix,
        EnableSsl = s.EnableSsl,
        SendMailOnComment = s.SendMailOnComment,
        SearchButtonText = s.SearchButtonText,
        SearchCommentLabelText = s.SearchCommentLabelText,
        SearchDefaultText = s.SearchDefaultText,
        EnableCommentSearch = s.EnableCommentSearch,
        ShowIncludeCommentsOption = s.ShowIncludeCommentsOption,
        ContactFormMessage = s.ContactFormMessage,
        ContactThankMessage = s.ContactThankMessage,
        ContactErrorMessage = s.ContactErrorMessage,
        EnableContactAttachments = s.EnableContactAttachments,
        EnableRecaptchaOnContactForm = s.EnableRecaptchaOnContactForm,
        ErrorTitle = s.ErrorTitle,
        ErrorText = s.ErrorText,
        HtmlHeader = s.HtmlHeader,
        TrackingScript = s.TrackingScript,
        DaysCommentsAreEnabled = s.DaysCommentsAreEnabled,
        IsCommentsEnabled = s.IsCommentsEnabled,
        EnableCommentsModeration = s.EnableCommentsModeration,
        IsCommentNestingEnabled = s.IsCommentNestingEnabled,
        Avatar = s.Avatar,
        EnablePingBackSend = s.EnablePingBackSend,
        EnablePingBackReceive = s.EnablePingBackReceive,
        EnableTrackBackSend = s.EnableTrackBackSend,
        EnableTrackBackReceive = s.EnableTrackBackReceive,
        CommentsPerPage = s.CommentsPerPage,
        EnableCountryInComments = s.EnableCountryInComments,
        EnableWebsiteInComments = s.EnableWebsiteInComments,
        ShowLivePreview = s.ShowLivePreview,
        CommentProvider = s.CommentProvider,
        DisqusDevMode = s.DisqusDevMode,
        DisqusAddCommentsToPages = s.DisqusAddCommentsToPages,
        DisqusWebsiteName = s.DisqusWebsiteName,
        FacebookAppId = s.FacebookAppId,
        FacebookLanguage = string.IsNullOrEmpty(s.FacebookLanguage) ? "en_US" : s.FacebookLanguage,
        TrustAuthenticatedUsers = s.TrustAuthenticatedUsers,
    };

    /// <summary>Copia i valori ricevuti nelle impostazioni (porting di SettingsRepository.Update).</summary>
    private static void Apply(SettingsDto n, BlogSettings s)
    {
        s.Name = n.Name ?? s.Name;
        s.Description = n.Description ?? string.Empty;
        s.PostsPerPage = n.PostsPerPage;
        s.ThemeCookieName = n.ThemeCookieName ?? string.Empty;
        s.UseBlogNameInPageTitles = n.UseBlogNameInPageTitles;
        s.EnableRelatedPosts = n.EnableRelatedPosts;
        s.EnableRating = n.EnableRating;
        s.ShowDescriptionInPostList = n.ShowDescriptionInPostList;
        s.DescriptionCharacters = n.DescriptionCharacters;
        s.ShowDescriptionInPostListForPostsByTagOrCategory = n.ShowDescriptionInPostListForPostsByTagOrCategory;
        s.DescriptionCharactersForPostsByTagOrCategory = n.DescriptionCharactersForPostsByTagOrCategory;
        s.TimeStampPostLinks = n.TimeStampPostLinks;
        s.ShowPostNavigation = n.ShowPostNavigation;
        s.Culture = n.Culture ?? s.Culture;
        s.TimeZoneId = n.TimeZoneId ?? s.TimeZoneId;
        s.RedirectToRemoveFileExtension = n.RedirectToRemoveFileExtension;
        s.Theme = n.DesktopTheme ?? s.Theme;
        s.HandleWwwSubdomain = n.HandleWwwSubdomain ?? string.Empty;
        s.EnableHttpCompression = n.EnableHttpCompression;
        s.EnableOpenSearch = n.EnableOpenSearch;
        s.RequireSslMetaWeblogApi = n.RequireSslForMetaWeblogApi;
        s.EnablePasswordReset = n.EnablePasswordReset;
        s.EnableSelfRegistration = n.EnableSelfRegistration;
        s.AllowServerToDownloadRemoteFiles = n.AllowServerToDownloadRemoteFiles;
        s.RemoteFileDownloadTimeout = n.RemoteFileDownloadTimeout;
        s.RemoteMaxFileSize = n.RemoteMaxFileSize;
        s.SelfRegistrationInitialRole = n.SelfRegistrationInitialRole ?? s.SelfRegistrationInitialRole;
        s.AuthorName = n.AuthorName ?? string.Empty;
        s.FeedAuthor = n.FeedAuthor ?? string.Empty;
        s.Endorsement = n.Endorsement ?? string.Empty;
        s.AlternateFeedUrl = n.AlternateFeedUrl ?? string.Empty;
        s.Language = n.Language ?? s.Language;
        s.PostsPerFeed = n.PostsPerFeed;
        s.EnableEnclosures = n.EnableEnclosures;
        s.SyndicationFormat = n.SyndicationFormat ?? s.SyndicationFormat;
        s.EnableTagExport = n.EnableTagExport;
        s.Email = n.Email ?? string.Empty;
        s.SmtpServer = n.SmtpServer ?? string.Empty;
        s.SmtpServerPort = n.SmtpServerPort;
        s.SmtpUserName = n.SmtpUserName ?? string.Empty;
        s.SmtpPassword = n.SmtpPassword ?? string.Empty;
        s.EmailSubjectPrefix = n.EmailSubjectPrefix ?? string.Empty;
        s.EnableSsl = n.EnableSsl;
        s.SendMailOnComment = n.SendMailOnComment;
        s.SearchButtonText = n.SearchButtonText ?? string.Empty;
        s.SearchCommentLabelText = n.SearchCommentLabelText ?? string.Empty;
        s.SearchDefaultText = n.SearchDefaultText ?? string.Empty;
        s.EnableCommentSearch = n.EnableCommentSearch;
        s.ShowIncludeCommentsOption = n.ShowIncludeCommentsOption;
        s.ContactFormMessage = n.ContactFormMessage ?? string.Empty;
        s.ContactThankMessage = n.ContactThankMessage ?? string.Empty;
        s.ContactErrorMessage = n.ContactErrorMessage ?? string.Empty;
        s.EnableContactAttachments = n.EnableContactAttachments;
        s.EnableRecaptchaOnContactForm = n.EnableRecaptchaOnContactForm;
        s.ErrorTitle = n.ErrorTitle ?? string.Empty;
        s.ErrorText = n.ErrorText ?? string.Empty;
        s.HtmlHeader = n.HtmlHeader ?? string.Empty;
        s.TrackingScript = n.TrackingScript ?? string.Empty;
        s.DaysCommentsAreEnabled = n.DaysCommentsAreEnabled;
        s.IsCommentsEnabled = n.IsCommentsEnabled;
        s.EnableCommentsModeration = n.EnableCommentsModeration;
        s.IsCommentNestingEnabled = n.IsCommentNestingEnabled;
        s.Avatar = n.Avatar ?? s.Avatar;
        s.EnablePingBackSend = n.EnablePingBackSend;
        s.EnablePingBackReceive = n.EnablePingBackReceive;
        s.EnableTrackBackSend = n.EnableTrackBackSend;
        s.EnableTrackBackReceive = n.EnableTrackBackReceive;
        s.CommentsPerPage = n.CommentsPerPage;
        s.EnableCountryInComments = n.EnableCountryInComments;
        s.EnableWebsiteInComments = n.EnableWebsiteInComments;
        s.ShowLivePreview = n.ShowLivePreview;
        s.CommentProvider = n.CommentProvider;
        s.DisqusDevMode = n.DisqusDevMode;
        s.DisqusAddCommentsToPages = n.DisqusAddCommentsToPages;
        s.DisqusWebsiteName = n.DisqusWebsiteName ?? string.Empty;
        s.FacebookAppId = n.FacebookAppId ?? string.Empty;
        s.FacebookLanguage = n.FacebookLanguage ?? s.FacebookLanguage;
        s.TrustAuthenticatedUsers = n.TrustAuthenticatedUsers;
    }
}
