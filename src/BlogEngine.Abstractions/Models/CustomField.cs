namespace BlogEngine.Models;

/// <summary>
/// Campo personalizzato associato a un oggetto (post, pagina, profilo, tema...).
/// </summary>
public sealed class CustomField
{
    public const string PostType = "POST";
    public const string PageType = "PAGE";
    public const string ProfileType = "PROFILE";
    public const string ThemeType = "THEME";

    public Guid BlogId { get; set; }

    /// <summary>Tipo dell'oggetto: <see cref="PostType"/>, <see cref="PageType"/>, ...</summary>
    public string CustomType { get; set; } = string.Empty;

    /// <summary>Identificativo dell'oggetto (id del post, nome del tema, ...).</summary>
    public string ObjectId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Attribute { get; set; } = string.Empty;
}
