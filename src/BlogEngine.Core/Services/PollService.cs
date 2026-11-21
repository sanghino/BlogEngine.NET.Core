using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlogEngine.Core.Data;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Quando i visitatori vedono i risultati di un sondaggio (in amministrazione si vedono sempre).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PollResultsVisibility
{
    /// <summary>Dopo aver votato, e da tutti a sondaggio concluso.</summary>
    AfterVote,

    /// <summary>Sempre, anche prima di votare.</summary>
    Always,

    /// <summary>Solo a sondaggio concluso.</summary>
    AfterClose,
}

/// <summary>Stato di un sondaggio in un certo momento.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PollStatus
{
    /// <summary>Non ancora iniziato (data di inizio nel futuro).</summary>
    Scheduled,

    Open,

    /// <summary>Scaduto o chiuso a mano: non si vota più, i risultati restano nello storico.</summary>
    Closed,
}

public sealed class PollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

/// <summary>Un voto: le risposte scelte e le chiavi anonime che impediscono di votare di nuovo.</summary>
public sealed class PollVote
{
    public DateTime DateUtc { get; set; }

    public List<Guid> Options { get; set; } = [];

    /// <summary>
    /// Impronte (HMAC) del votante: utente collegato, cookie del browser, rete e browser. Non sono
    /// reversibili: dal file non si risale a nomi utente o indirizzi IP.
    /// </summary>
    public List<string> Keys { get; set; } = [];
}

public sealed class Poll
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Question { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Più risposte per voto; altrimenti una sola.</summary>
    public bool MultipleChoice { get; set; }

    /// <summary>Numero massimo di risposte con la scelta multipla (0 = nessun limite).</summary>
    public int MaxChoices { get; set; }

    public List<PollOption> Options { get; set; } = [];

    public DateTime CreatedUtc { get; set; }

    /// <summary>Inizio delle votazioni (null = subito).</summary>
    public DateTime? StartsUtc { get; set; }

    /// <summary>Scadenza (null = nessuna scadenza, si chiude a mano).</summary>
    public DateTime? EndsUtc { get; set; }

    /// <summary>Chiusura anticipata dall'amministrazione.</summary>
    public DateTime? ClosedUtc { get; set; }

    /// <summary>Votano solo gli utenti collegati.</summary>
    public bool RegisteredOnly { get; set; }

    public PollResultsVisibility ResultsVisibility { get; set; } = PollResultsVisibility.AfterVote;

    public List<PollVote> Votes { get; set; } = [];

    public PollStatus StatusAt(DateTime utc) =>
        ClosedUtc is not null || (EndsUtc is { } end && utc >= end) ? PollStatus.Closed
        : StartsUtc is { } start && utc < start ? PollStatus.Scheduled
        : PollStatus.Open;

    /// <summary>Quando il sondaggio si è concluso (chiusura manuale o scadenza), se è concluso.</summary>
    public DateTime? EndedUtc(DateTime utc) =>
        ClosedUtc ?? (EndsUtc is { } end && utc >= end ? end : null);

    /// <summary>Voti per risposta, nell'ordine delle risposte.</summary>
    public IReadOnlyList<(PollOption Option, int Votes)> Results() =>
        Options.Select(o => (o, Votes.Count(v => v.Options.Contains(o.Id)))).ToList();
}

public enum PollVoteResult
{
    Accepted,
    NotFound,
    NotOpen,
    AlreadyVoted,
    LoginRequired,
    InvalidChoice,
}

/// <summary>
/// Sondaggi del widget Poll: domande, risposte, scadenze e voti, con lo storico dei sondaggi conclusi.
/// Sono salvati nell'archivio delle estensioni (XML o SQLite, come il resto del blog).
/// </summary>
public sealed class PollService(IExtensionDataStore store, IBlogContext context, IBlogClock clock)
{
    public const int MaxOptions = 20;
    public const int MaxTextLength = 300;
    private const string Key = "polls";
    private const string SecretKey = "polls-secret";
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public DateTime UtcNow => clock.UtcNow;

    /// <summary>Tutti i sondaggi, dal più recente.</summary>
    public async Task<IReadOnlyList<Poll>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).OrderByDescending(p => p.CreatedUtc).ToList();

    public async Task<Poll?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);

    /// <summary>Il sondaggio aperto più recente (quello che il widget mostra se non ne è stato scelto uno).</summary>
    public async Task<Poll?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return (await GetAllAsync(cancellationToken)).FirstOrDefault(p => p.StatusAt(now) == PollStatus.Open);
    }

    /// <summary>
    /// Crea o aggiorna un sondaggio (domanda, risposte, date, opzioni). I voti non si modificano da qui;
    /// una risposta che ha già ricevuto voti non si può togliere.
    /// </summary>
    public async Task<(Poll? Poll, string? Error)> SaveAsync(Poll changes, CancellationToken cancellationToken = default)
    {
        var question = Clean(changes.Question);
        var options = changes.Options
            .Select(o => new PollOption { Id = o.Id == Guid.Empty ? Guid.NewGuid() : o.Id, Text = Clean(o.Text) })
            .Where(o => o.Text.Length > 0)
            .ToList();
        if (question.Length == 0)
        {
            return (null, "question");
        }

        if (options.Count < 2 || options.Count > MaxOptions)
        {
            return (null, "options");
        }

        if (changes.StartsUtc is { } start && changes.EndsUtc is { } end && end <= start)
        {
            return (null, "dates");
        }

        Poll? saved = null;
        string? error = null;
        await UpdateAsync(polls =>
        {
            var poll = polls.FirstOrDefault(p => p.Id == changes.Id);
            if (poll is null)
            {
                poll = new Poll { Id = changes.Id == Guid.Empty ? Guid.NewGuid() : changes.Id, CreatedUtc = clock.UtcNow };
                polls.Add(poll);
            }
            else
            {
                var voted = poll.Votes.SelectMany(v => v.Options).ToHashSet();
                if (poll.Options.Any(o => voted.Contains(o.Id) && options.All(n => n.Id != o.Id)))
                {
                    error = "optionHasVotes";
                    return false;
                }
            }

            poll.Question = question;
            poll.Description = (changes.Description ?? string.Empty).Trim();
            poll.MultipleChoice = changes.MultipleChoice;
            poll.MaxChoices = changes.MultipleChoice ? Math.Clamp(changes.MaxChoices, 0, options.Count) : 1;
            poll.Options = options;
            poll.StartsUtc = changes.StartsUtc;
            poll.EndsUtc = changes.EndsUtc;
            poll.RegisteredOnly = changes.RegisteredOnly;
            poll.ResultsVisibility = changes.ResultsVisibility;
            saved = poll;
            return true;
        }, cancellationToken);

        return (saved, error);
    }

    /// <summary>Chiude subito un sondaggio aperto, oppure lo riapre (togliendo anche una scadenza già passata).</summary>
    public Task<bool> SetClosedAsync(Guid id, bool closed, CancellationToken cancellationToken = default) =>
        UpdateAsync(polls =>
        {
            if (polls.FirstOrDefault(p => p.Id == id) is not { } poll)
            {
                return false;
            }

            var now = clock.UtcNow;
            poll.ClosedUtc = closed ? poll.ClosedUtc ?? now : null;
            if (!closed && poll.EndsUtc is { } end && end <= now)
            {
                poll.EndsUtc = null;
            }

            return true;
        }, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        UpdateAsync(polls => polls.RemoveAll(p => p.Id == id) > 0, cancellationToken);

    /// <summary>Le impronte del votante per questo sondaggio (HMAC con una chiave segreta del blog).</summary>
    public async Task<IReadOnlyList<string>> VoterKeysAsync(Guid pollId, string? userName, string? browserToken, string? network, CancellationToken cancellationToken = default)
    {
        var secret = await SecretAsync(cancellationToken);
        string Hash(string kind, string value) =>
            kind + ":" + Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{pollId:N}|{kind}|{value}")))[..32];

        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(userName))
        {
            keys.Add(Hash("user", userName.Trim().ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(browserToken))
        {
            keys.Add(Hash("browser", browserToken));
        }

        if (!string.IsNullOrWhiteSpace(network))
        {
            keys.Add(Hash("network", network));
        }

        return keys;
    }

    /// <summary>Per quanto tempo l'impronta di rete e browser impedisce un nuovo voto.</summary>
    public static readonly TimeSpan NetworkWindow = TimeSpan.FromHours(24);

    public static bool HasVoted(Poll poll, IReadOnlyList<string> voterKeys, DateTime utcNow)
    {
        // Nome utente e cookie del browser valgono per sempre. L'impronta di rete e browser (per chi cancella i
        // cookie) vale solo 24 ore: la stessa rete e lo stesso browser possono averli anche persone diverse
        // (famiglia, ufficio, telefoni dietro lo stesso indirizzo dell'operatore). Non blocca gli utenti collegati,
        // che sono già riconosciuti dal nome.
        var isUser = voterKeys.Any(k => k.StartsWith("user:", StringComparison.Ordinal));
        var lasting = voterKeys.Where(k => !k.StartsWith("network:", StringComparison.Ordinal)).ToList();
        var network = isUser ? [] : voterKeys.Where(k => k.StartsWith("network:", StringComparison.Ordinal)).ToList();
        return poll.Votes.Any(v => v.Keys.Any(lasting.Contains) || (utcNow - v.DateUtc < NetworkWindow && v.Keys.Any(network.Contains)));
    }

    public async Task<PollVoteResult> VoteAsync(Guid pollId, IReadOnlyCollection<Guid> choices, IReadOnlyList<string> voterKeys, bool isAuthenticated, CancellationToken cancellationToken = default)
    {
        var result = PollVoteResult.NotFound;
        await UpdateAsync(polls =>
        {
            if (polls.FirstOrDefault(p => p.Id == pollId) is not { } poll)
            {
                result = PollVoteResult.NotFound;
                return false;
            }

            var selected = choices.Distinct().ToList();
            result = poll.StatusAt(clock.UtcNow) != PollStatus.Open ? PollVoteResult.NotOpen
                : poll.RegisteredOnly && !isAuthenticated ? PollVoteResult.LoginRequired
                : voterKeys.Count == 0 || HasVoted(poll, voterKeys, clock.UtcNow) ? PollVoteResult.AlreadyVoted
                : selected.Count == 0 || selected.Any(c => poll.Options.All(o => o.Id != c))
                    || (!poll.MultipleChoice && selected.Count > 1)
                    || (poll.MultipleChoice && poll.MaxChoices > 0 && selected.Count > poll.MaxChoices) ? PollVoteResult.InvalidChoice
                : PollVoteResult.Accepted;
            if (result != PollVoteResult.Accepted)
            {
                return false;
            }

            poll.Votes.Add(new PollVote { DateUtc = clock.UtcNow, Options = selected, Keys = voterKeys.ToList() });
            return true;
        }, cancellationToken);

        return result;
    }

    private static string Clean(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length > MaxTextLength ? value[..MaxTextLength] : value;
    }

    private async Task<List<Poll>> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, Key, cancellationToken);
        try
        {
            return json is null ? [] : JsonSerializer.Deserialize<List<Poll>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Modifica l'elenco sotto lucchetto; salva solo se <paramref name="change"/> restituisce true.</summary>
    private async Task<bool> UpdateAsync(Func<List<Poll>, bool> change, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var polls = await LoadAsync(cancellationToken);
            if (!change(polls))
            {
                return false;
            }

            await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, Key, JsonSerializer.Serialize(polls, Json), cancellationToken);
            return true;
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<byte[]> SecretAsync(CancellationToken cancellationToken)
    {
        var saved = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, SecretKey, cancellationToken);
        if (!string.IsNullOrEmpty(saved))
        {
            try
            {
                return Convert.FromBase64String(saved);
            }
            catch (FormatException)
            {
                // Valore rovinato: se ne crea uno nuovo (i voti già dati restano, ma le loro impronte non coincidono più).
            }
        }

        var secret = RandomNumberGenerator.GetBytes(32);
        await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, SecretKey, Convert.ToBase64String(secret), cancellationToken);
        return secret;
    }
}
