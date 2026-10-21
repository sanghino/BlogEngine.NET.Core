// Trasforma gli emoticon scritti nei commenti, come :) o :D, in immagini.
// Porting dell'estensione Smilies di BlogEngine.NET (usa le immagini del plugin emoticons di TinyMCE).

[Extension("Converts ASCII smilies into real smilies in the comments", "4.0", "BlogEngine.NET")]
public sealed class Smilies : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<ServingEvent<Comment>>, SmiliesHandler>();
}

public sealed class SmiliesHandler(IPageResources page) : IEventHandler<ServingEvent<Comment>>
{
    // Il testo dei commenti arriva già codificato: l'apostrofo di :'( è &#39;.
    private static readonly (string Code, string Name, string Alt)[] Codes =
    [
        ("(H)", "cool", "Cool"), (":&#39;(", "cry", "Cry"), (":'(", "cry", "Cry"), (":$", "embarassed", "Embarassed"),
        (":|", "foot-in-mouth", "Foot"), (":(", "frown", "Frown"), ("(A)", "innocent", "Innocent"), ("(K)", "kiss", "Kiss"),
        (":D", "laughing", "Laughing"), ("($)", "money-mouth", "Money"), (":-#", "sealed", "Sealed"), (":-)", "smile", "Smile"),
        (":)", "smile", "Smile"), (":-O", "surprised", "Surprised"), (":P", "tongue-out", "Tong"), ("*-)", "undecided", "Undecided"),
        (";-)", "wink", "Wink"), ("8o|", "yell", "Yell"),
    ];

    public Task HandleAsync(ServingEvent<Comment> blogEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(blogEvent.Body))
        {
            return Task.CompletedTask;
        }

        var body = blogEvent.Body;
        foreach (var (code, name, alt) in Codes)
        {
            body = body.Replace(code, $"<img src=\"{page.ApplicationRoot}admin/editors/tinymce/plugins/emoticons/img/smiley-{name}.gif\" class=\"flag\" alt=\"{alt}\" />", StringComparison.Ordinal);
        }

        blogEvent.Body = body;
        return Task.CompletedTask;
    }
}
