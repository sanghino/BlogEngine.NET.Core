using BlogEngine.Core.Services;
using BlogEngine.Services;

namespace BlogEngine.Web.Themes;

/// <summary>Un sondaggio visto da un certo visitatore, per il widget, la pagina /polls e la risposta al voto.</summary>
public sealed class PollModel
{
    public string Id { get; init; } = string.Empty;

    public string Question { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>"scheduled", "open" o "closed".</summary>
    public string Status { get; init; } = string.Empty;

    public bool MultipleChoice { get; init; }

    public int MaxChoices { get; init; }

    public bool HasVoted { get; init; }

    public bool CanVote { get; init; }

    /// <summary>Solo per utenti registrati e il visitatore non è collegato.</summary>
    public bool LoginRequired { get; init; }

    public bool ShowResults { get; init; }

    /// <summary>Numero di votanti.</summary>
    public int Voters { get; init; }

    public DateTime? Starts { get; init; }

    /// <summary>Scadenza, nell'ora del blog.</summary>
    public DateTime? Ends { get; init; }

    /// <summary>Data di conclusione (scadenza o chiusura), nell'ora del blog.</summary>
    public DateTime? Ended { get; init; }

    public IReadOnlyList<PollOptionModel> Options { get; init; } = [];
}

public sealed class PollOptionModel
{
    public string Id { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>Voti e percentuale sui votanti; zero se i risultati non sono visibili a questo visitatore.</summary>
    public int Votes { get; init; }

    public int Percent { get; init; }

    public bool Winner { get; init; }
}

/// <summary>Come vede un sondaggio il visitatore corrente: se ha già votato, se può votare, se vede i risultati.</summary>
public sealed class PollPresenter(PollService polls, IBlogContext context, IBlogClock clock, IHttpContextAccessor http)
{
    /// <summary>Cookie con un numero casuale che identifica il browser (non contiene dati personali).</summary>
    public const string VoterCookie = "poll_voter";

    public async Task<IReadOnlyList<string>> VoterKeysAsync(Guid pollId, CancellationToken cancellationToken = default)
    {
        var request = http.HttpContext?.Request;
        var browser = request?.Cookies[VoterCookie];
        var network = request is null ? null : $"{http.HttpContext!.Connection.RemoteIpAddress}|{request.Headers.UserAgent}";
        return await polls.VoterKeysAsync(pollId, context.UserName, browser, network, cancellationToken);
    }

    /// <summary>Il valore del cookie del browser, creato al primo voto.</summary>
    public string EnsureVoterCookie()
    {
        var httpContext = http.HttpContext!;
        var token = httpContext.Request.Cookies[VoterCookie];
        if (string.IsNullOrEmpty(token) || token.Length > 64)
        {
            token = Guid.NewGuid().ToString("N");
        }

        httpContext.Response.Cookies.Append(VoterCookie, token, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(2),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Path = "/",
        });
        return token;
    }

    public async Task<PollModel> ToModelAsync(Poll poll, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var status = poll.StatusAt(now);
        var hasVoted = PollService.HasVoted(poll, await VoterKeysAsync(poll.Id, cancellationToken), now);
        var loggedIn = !string.IsNullOrEmpty(context.UserName);
        var showResults = status == PollStatus.Closed
            || poll.ResultsVisibility == PollResultsVisibility.Always
            || (poll.ResultsVisibility == PollResultsVisibility.AfterVote && hasVoted);
        var results = poll.Results();
        var max = results.Count == 0 ? 0 : results.Max(r => r.Votes);
        var voters = poll.Votes.Count;

        async Task<DateTime?> Local(DateTime? utc) => utc is { } value ? await clock.ToBlogTimeAsync(value) : null;

        return new PollModel
        {
            Id = poll.Id.ToString("N"),
            Question = poll.Question,
            Description = poll.Description,
            Status = status.ToString().ToLowerInvariant(),
            MultipleChoice = poll.MultipleChoice,
            MaxChoices = poll.MultipleChoice ? poll.MaxChoices : 1,
            HasVoted = hasVoted,
            LoginRequired = poll.RegisteredOnly && !loggedIn,
            CanVote = status == PollStatus.Open && !hasVoted && (!poll.RegisteredOnly || loggedIn),
            ShowResults = showResults,
            Voters = showResults ? voters : 0,
            Starts = await Local(poll.StartsUtc),
            Ends = await Local(poll.EndsUtc),
            Ended = await Local(poll.EndedUtc(now)),
            Options = results.Select(r => new PollOptionModel
            {
                Id = r.Option.Id.ToString("N"),
                Text = r.Option.Text,
                Votes = showResults ? r.Votes : 0,
                Percent = showResults && voters > 0 ? (int)Math.Round(100.0 * r.Votes / voters) : 0,
                Winner = showResults && max > 0 && r.Votes == max,
            }).ToList(),
        };
    }
}

/// <summary>
/// Widget Poll: mostra il sondaggio scelto nelle impostazioni, oppure il più recente ancora aperto.
/// Se il sondaggio scelto è concluso mostra i suoi risultati.
/// </summary>
public sealed class PollWidget(PollService polls, PollPresenter presenter, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "Poll";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var chosen = widget.Text("poll", string.Empty);
        var poll = Guid.TryParse(chosen, out var id)
            ? await polls.GetAsync(id, cancellationToken)
            : await polls.GetCurrentAsync(cancellationToken);
        var hasArchive = (await polls.GetAllAsync(cancellationToken)).Any(p => p.StatusAt(polls.UtcNow) == PollStatus.Closed);
        return new
        {
            poll = poll is null || poll.StatusAt(polls.UtcNow) == PollStatus.Scheduled ? null : await presenter.ToModelAsync(poll, cancellationToken),
            archive_url = hasArchive ? links.BlogRoot + "polls" : string.Empty,
            login_url = links.BlogRoot + "account/login",
        };
    }
}
