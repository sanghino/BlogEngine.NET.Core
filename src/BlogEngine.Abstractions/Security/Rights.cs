namespace BlogEngine.Security;

/// <summary>
/// Permessi assegnabili ai ruoli. Il ruolo amministratore li possiede sempre tutti (tranne <see cref="None"/>).
/// I nomi sono memorizzati come testo: non vanno rinominati.
/// </summary>
public enum Rights
{
    None = 0,

    [RightDetails(RightCategory.General)] ViewDetailedErrorMessages,
    [RightDetails(RightCategory.General)] ViewDashboard,
    [RightDetails(RightCategory.General)] AccessAdminPages,
    [RightDetails(RightCategory.General)] AccessAdminSettingsPages,

    [RightDetails(RightCategory.Comments)] ViewPublicComments,
    [RightDetails(RightCategory.Comments)] ViewUnmoderatedComments,
    [RightDetails(RightCategory.Comments)] CreateComments,
    [RightDetails(RightCategory.Comments)] ModerateComments,

    [RightDetails(RightCategory.Posts)] ViewPublicPosts,
    [RightDetails(RightCategory.Posts)] ViewUnpublishedPosts,
    [RightDetails(RightCategory.Posts)] CreateNewPosts,
    [RightDetails(RightCategory.Posts)] EditOwnPosts,
    [RightDetails(RightCategory.Posts)] EditOtherUsersPosts,
    [RightDetails(RightCategory.Posts)] DeleteOwnPosts,
    [RightDetails(RightCategory.Posts)] DeleteOtherUsersPosts,
    [RightDetails(RightCategory.Posts)] PublishOwnPosts,
    [RightDetails(RightCategory.Posts)] PublishOtherUsersPosts,

    [RightDetails(RightCategory.Pages)] ViewPublicPages,
    [RightDetails(RightCategory.Pages)] ViewUnpublishedPages,
    [RightDetails(RightCategory.Pages)] CreateNewPages,
    [RightDetails(RightCategory.Pages)] EditOwnPages,

    [RightDetails(RightCategory.Posts)] ViewRatingsOnPosts,
    [RightDetails(RightCategory.Posts)] SubmitRatingsOnPosts,

    [RightDetails(RightCategory.Roles)] ViewRoles,
    [RightDetails(RightCategory.Roles)] CreateNewRoles,
    [RightDetails(RightCategory.Roles)] EditRoles,
    [RightDetails(RightCategory.Roles)] DeleteRoles,
    [RightDetails(RightCategory.Roles)] EditOwnRoles,
    [RightDetails(RightCategory.Roles)] EditOtherUsersRoles,

    [RightDetails(RightCategory.Users)] CreateNewUsers,
    [RightDetails(RightCategory.Users)] DeleteUserSelf,
    [RightDetails(RightCategory.Users)] DeleteUsersOtherThanSelf,
    [RightDetails(RightCategory.Users)] EditOwnUser,
    [RightDetails(RightCategory.Users)] EditOtherUsers,

    [RightDetails(RightCategory.Custom)] ManageExtensions,
    [RightDetails(RightCategory.Custom)] ManageWidgets,
    [RightDetails(RightCategory.Custom)] ManageThemes,
    [RightDetails(RightCategory.Custom)] ManagePackages,
}

/// <summary>Raggruppamento dei permessi nell'interfaccia di amministrazione.</summary>
public enum RightCategory
{
    None,
    General,
    Comments,
    Pages,
    Posts,
    Users,
    Roles,
    Custom,
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class RightDetailsAttribute(RightCategory category) : Attribute
{
    public RightCategory Category { get; } = category;
}

/// <summary>Modalità di verifica di più permessi insieme.</summary>
public enum AuthorizationCheck
{
    /// <summary>Basta uno dei permessi.</summary>
    HasAny,

    /// <summary>Servono tutti i permessi.</summary>
    HasAll,
}
