namespace BlogEngine.Core.Services;

/// <summary>Contenuti predefiniti di un nuovo blog, ripresi dai file di installazione della versione originale.</summary>
public static class DefaultContent
{
    /// <summary>
    /// Servizi di ping (pingservices.xml). Degli otto originali è ancora attivo solo Ping-O-Matic, che inoltra
    /// a sua volta ai principali aggregatori; gli altri (Technorati, Google Blog Search, Bloglines...) hanno chiuso.
    /// </summary>
    public static readonly IReadOnlyList<string> PingServices =
    [
        "https://rpc.pingomatic.com/",
    ];

    /// <summary>Parole ignorate dalla ricerca (stopwords.txt).</summary>
    public static readonly IReadOnlyList<string> StopWords =
    [
        "a", "about", "actually", "add", "after", "all", "almost", "along", "also", "an", "and", "any", "are", "as", "at",
        "be", "both", "but", "by", "can", "cannot", "could", "com", "de", "do", "down", "each", "either", "en", "for",
        "from", "good", "has", "have", "he", "her", "here", "hers", "his", "how", "i", "if", "in", "into", "is", "it",
        "its", "just", "la", "like", "long", "make", "me", "my", "more", "much", "need", "new", "now", "of", "off", "on",
        "once", "one", "ones", "only", "or", "our", "out", "over", "own", "really", "right", "same", "see", "she", "so",
        "some", "such", "take", "takes", "took", "that", "the", "their", "these", "thing", "this", "to", "too", "very",
        "was", "we", "well", "what", "when", "where", "who", "will", "with", "www", "und", "up", "use", "used", "using",
        "you", "your",
    ];

    public const string DefaultCategoryTitle = "General";

    public const string DefaultCategoryDescription = "Generic topics";
}
