using System.Globalization;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Sondaggi del widget Poll (Contenuto → Sondaggi). Serve il permesso di gestire i widget.</summary>
[Route("api/polls")]
public sealed class PollsAdminController(PollService polls, IRightsService rights, IBlogClock clock, Themes.PollPresenter presenter) : AdminApiController
{
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm";

    [HttpGet("")]
    public async Task<IEnumerable<PollItem>> Get()
    {
        await DemandAsync();
        var items = new List<PollItem>();
        foreach (var poll in await polls.GetAllAsync())
        {
            items.Add(await ToItemAsync(poll));
        }

        return items;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandAsync();
        return await polls.GetAsync(id) is { } poll ? Ok(await ToItemAsync(poll)) : NotFound();
    }

    [HttpPost("")]
    public Task<IActionResult> Post([FromBody] PollItem item) => SaveAsync(item with { Id = Guid.Empty });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Put(Guid id, [FromBody] PollItem item)
    {
        await DemandAsync();
        return await polls.GetAsync(id) is null ? NotFound() : await SaveAsync(item with { Id = id });
    }

    /// <summary>Chiude subito il sondaggio ("close") o lo riapre ("reopen").</summary>
    [HttpPut("{id:guid}/{command:regex(^(close|reopen)$)}")]
    public async Task<IActionResult> Close(Guid id, string command)
    {
        await DemandAsync();
        return await polls.SetClosedAsync(id, command == "close") ? Ok(await ToItemAsync((await polls.GetAsync(id))!)) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await DemandAsync();
        await polls.DeleteAsync(id);
        return Ok();
    }

    private async Task<IActionResult> SaveAsync(PollItem item)
    {
        await DemandAsync();
        DateTime? starts, ends;
        try
        {
            starts = await ToUtcAsync(item.Starts);
            ends = await ToUtcAsync(item.Ends);
        }
        catch (FormatException)
        {
            return BadRequest(T("pollErrorDates"));
        }

        var (poll, error) = await polls.SaveAsync(new Poll
        {
            Id = item.Id,
            Question = item.Question ?? string.Empty,
            Description = item.Description ?? string.Empty,
            MultipleChoice = item.MultipleChoice,
            MaxChoices = item.MaxChoices,
            Options = (item.Options ?? []).Select(o => new PollOption
            {
                Id = Guid.TryParse(o.Id, out var optionId) ? optionId : Guid.Empty,
                Text = o.Text ?? string.Empty,
            }).ToList(),
            StartsUtc = starts,
            EndsUtc = ends,
            RegisteredOnly = item.RegisteredOnly,
            Featured = item.Featured,
            ResultsVisibility = Enum.TryParse<PollResultsVisibility>(item.ResultsVisibility, true, out var visibility) ? visibility : PollResultsVisibility.AfterVote,
        });

        if (poll is null)
        {
            return BadRequest(T(error switch
            {
                "question" => "pollErrorQuestion",
                "options" => "pollErrorOptions",
                "dates" => "pollErrorDates",
                "optionHasVotes" => "pollErrorOptionHasVotes",
                _ => "errorInvalidRequest",
            }));
        }

        return item.Id == Guid.Empty ? StatusCode(StatusCodes.Status201Created, await ToItemAsync(poll)) : Ok(await ToItemAsync(poll));
    }

    private async Task<PollItem> ToItemAsync(Poll poll)
    {
        var now = polls.UtcNow;
        var voters = poll.Votes.Count;
        var results = poll.Results();
        var percents = Poll.Percentages(results.Select(r => r.Votes).ToList());
        async Task<string> Local(DateTime? utc) =>
            utc is { } value ? (await clock.ToBlogTimeAsync(value)).ToString(DateFormat, CultureInfo.InvariantCulture) : string.Empty;

        return new PollItem
        {
            Id = poll.Id,
            Question = poll.Question,
            Description = poll.Description,
            MultipleChoice = poll.MultipleChoice,
            MaxChoices = poll.MultipleChoice ? poll.MaxChoices : 0,
            Options = results.Select((r, i) => new PollOptionItem
            {
                Id = r.Option.Id.ToString(),
                Text = r.Option.Text,
                Votes = r.Votes,
                Percent = percents[i],
            }).ToList(),
            Starts = await Local(poll.StartsUtc),
            Ends = await Local(poll.EndsUtc),
            Created = await Local(poll.CreatedUtc),
            Ended = await Local(poll.EndedUtc(now)),
            RegisteredOnly = poll.RegisteredOnly,
            Featured = poll.Featured,
            Url = presenter.Url(poll),
            ResultsVisibility = poll.ResultsVisibility.ToString(),
            Status = poll.StatusAt(now).ToString().ToLowerInvariant(),
            Voters = voters,
            LastVote = await Local(poll.Votes.Count == 0 ? null : poll.Votes.Max(v => v.DateUtc)),
        };
    }

    /// <summary>Le date arrivano nell'ora del blog ("2026-11-30T18:00"); vuote = nessuna data.</summary>
    private async Task<DateTime?> ToUtcAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var local = DateTime.ParseExact(text.Trim()[..Math.Min(16, text.Trim().Length)], DateFormat, CultureInfo.InvariantCulture);
        return await clock.ToUtcAsync(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ManageWidgets))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

public sealed record PollItem
{
    public Guid Id { get; init; }

    public string? Question { get; init; }

    public string? Description { get; init; }

    public bool MultipleChoice { get; init; }

    public int MaxChoices { get; init; }

    public List<PollOptionItem>? Options { get; init; }

    /// <summary>Inizio e scadenza nell'ora del blog, "yyyy-MM-ddTHH:mm" (vuoto = nessuna).</summary>
    public string? Starts { get; init; }

    public string? Ends { get; init; }

    public bool RegisteredOnly { get; init; }

    /// <summary>In evidenza: barra in cima alle pagine finché il sondaggio è aperto.</summary>
    public bool Featured { get; init; }

    /// <summary>AfterVote, Always o AfterClose.</summary>
    public string? ResultsVisibility { get; init; }

    // Solo in lettura.
    public string Status { get; init; } = string.Empty;

    public int Voters { get; init; }

    public string Created { get; init; } = string.Empty;

    public string Ended { get; init; } = string.Empty;

    public string LastVote { get; init; } = string.Empty;

    /// <summary>Indirizzo della pagina pubblica del sondaggio.</summary>
    public string Url { get; init; } = string.Empty;
}

public sealed record PollOptionItem
{
    public string? Id { get; init; }

    public string? Text { get; init; }

    public int Votes { get; init; }

    public int Percent { get; init; }
}
