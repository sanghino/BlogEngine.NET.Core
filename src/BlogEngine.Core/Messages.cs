using System.Globalization;

namespace BlogEngine.Core;

/// <summary>
/// Chiavi dei messaggi mostrati agli utenti (errori di validazione e operazioni non consentite).
/// Il Core non dipende dalla lingua: segnala la chiave, e il sito la traduce nella lingua del blog
/// con i file Resources/Labels.*.resx (inglese come lingua di base, italiano, ...).
/// I parametri si aggiungono con <see cref="Format"/> e sostituiscono {0}, {1}... nel testo tradotto.
/// </summary>
public static class Messages
{
    public const string TitleRequired = "validationTitleRequired";
    public const string ContentRequired = "validationContentRequired";
    public const string PageParentSelf = "validationPageParentSelf";
    public const string CategoryExists = "validationCategoryExists";
    public const string CategoryParentInvalid = "validationCategoryParentInvalid";
    public const string BlogUrlRequired = "validationBlogUrlRequired";
    public const string BlogNameRequired = "validationBlogNameRequired";
    public const string BlogNameInvalid = "validationBlogNameInvalid";
    public const string BlogNameReserved = "validationBlogNameReserved";
    public const string BlogExists = "validationBlogExists";
    public const string UserNameRequired = "validationUserNameRequired";
    public const string UserNameSimpleChars = "validationUserNameSimpleChars";
    public const string UserNameInvalid = "validationUserNameInvalid";
    public const string UserNameInUse = "validationUserNameInUse";
    public const string EmailInvalid = "validationEmailInvalid";
    public const string EmailInUse = "validationEmailInUse";
    public const string PasswordTooShort = "validationPasswordTooShort";
    public const string TypeRequired = "validationTypeRequired";
    public const string KeyRequired = "validationKeyRequired";
    public const string RoleNameRequired = "validationRoleNameRequired";
    public const string RoleExists = "validationRoleExists";

    public const string CommentsClosed = "errorCommentsClosed";
    public const string PrimaryBlogDelete = "errorPrimaryBlogDelete";
    public const string AdministratorRightsReadOnly = "errorAdministratorRightsReadOnly";
    public const string LastAdministrator = "errorLastAdministrator";
    public const string SystemRolesDelete = "errorSystemRolesDelete";
    public const string BlogNeedsAdministrator = "errorBlogNeedsAdministrator";
    public const string InvalidPath = "errorInvalidPath";
    public const string RootFolderDelete = "errorRootFolderDelete";

    /// <summary>Separatore tra la chiave e i suoi parametri.</summary>
    public const char ParameterSeparator = '|';

    /// <summary>Chiave con parametri, es. <c>Format(PasswordTooShort, 8)</c>.</summary>
    public static string Format(string key, params object[] parameters) =>
        parameters.Length == 0
            ? key
            : key + ParameterSeparator + string.Join(ParameterSeparator, parameters.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
}
