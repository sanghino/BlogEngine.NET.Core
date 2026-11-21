using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Public;

/// <summary>Voto dei sondaggi (widget Poll) e pagina pubblica /polls con i sondaggi conclusi.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
public sealed class PollsController(
    PollService polls,
    PollPresenter presenter,
    SiteRenderer site,
    PublicContent content,
    IBlogContext context) : Controller
{
    /// <summary>Registra il voto e restituisce il sondaggio aggiornato (con i risultati, se visibili).</summary>
    [HttpPost("/poll/vote")]
    [EnableRateLimiting(BlogActionsController.FormsRateLimit)]
    public async Task<IActionResult> Vote([FromForm] string? id, [FromForm] List<string>? options)
    {
        if (!Guid.TryParse(id, out var pollId) || await polls.GetAsync(pollId) is null)
        {
            return NotFound(new { message = site.T("pollNotFound") });
        }

        // Le risposte arrivano come più campi "options" o come un solo campo separato da virgole (blog.js).
        var choices = (options ?? [])
            .SelectMany(o => (o ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(o => Guid.TryParse(o, out var choice) ? choice : Guid.Empty)
            .ToList();
        if (choices.Count == 0 || choices.Contains(Guid.Empty))
        {
            return BadRequest(new { message = site.T("pollChooseAnswer") });
        }

        // Il cookie del browser serve già per il voto: viene creato prima di calcolare le impronte.
        var token = presenter.EnsureVoterCookie();
        Request.Cookies.TryGetValue(PollPresenter.VoterCookie, out var existing);
        var keys = (await presenter.VoterKeysAsync(pollId)).ToList();
        if (existing != token)
        {
            keys.AddRange(await polls.VoterKeysAsync(pollId, null, token, null));
        }

        var result = await polls.VoteAsync(pollId, choices, keys, !string.IsNullOrEmpty(context.UserName));
        var poll = await presenter.ToModelAsync((await polls.GetAsync(pollId))!);
        return result switch
        {
            PollVoteResult.Accepted => Json(new { status = "OK", message = site.T("pollThankYou"), poll }),
            PollVoteResult.AlreadyVoted => Json(new { status = "VOTED", message = site.T("pollAlreadyVoted"), poll }),
            PollVoteResult.NotOpen => Json(new { status = "CLOSED", message = site.T("pollClosed"), poll }),
            PollVoteResult.LoginRequired => StatusCode(StatusCodes.Status403Forbidden, new { message = site.T("pollLoginRequired") }),
            _ => BadRequest(new { message = site.T(result == PollVoteResult.InvalidChoice ? "pollChooseAnswer" : "pollNotFound") }),
        };
    }

    /// <summary>Pagina di un solo sondaggio (da linkare nei post o condividere), anche quando è concluso.</summary>
    [HttpGet("/poll/{key}")]
    public async Task<IActionResult> Details(string key)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return Redirect("~/account/login");
        }

        if (await polls.FindAsync(key) is not { } poll || poll.StatusAt(polls.UtcNow) == PollStatus.Scheduled)
        {
            return await site.PageAsync("error", new PageMeta { Title = site.T("pollNotFound"), NoIndex = true }, new Dictionary<string, object?>
            {
                ["error"] = new { code = StatusCodes.Status404NotFound, title = site.T("pollNotFound"), text = string.Empty, suggestions = Array.Empty<object>() },
            }, StatusCodes.Status404NotFound);
        }

        var model = await presenter.ToModelAsync(poll);
        return await site.PageAsync("poll", new PageMeta
        {
            Title = poll.Question,
            Description = string.IsNullOrWhiteSpace(poll.Description) ? poll.Question : poll.Description,
        }, new Dictionary<string, object?>
        {
            ["poll"] = model,
            ["polls_url"] = Url.Content("~/polls"),
        });
    }

    /// <summary>Sondaggi in corso e conclusi, con i risultati che il visitatore può vedere.</summary>
    [HttpGet("/polls")]
    public async Task<IActionResult> Archive()
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return Redirect("~/account/login");
        }

        var now = polls.UtcNow;
        var visible = (await polls.GetAllAsync()).Where(p => p.StatusAt(now) != PollStatus.Scheduled).ToList();
        var open = new List<PollModel>();
        var closed = new List<PollModel>();
        foreach (var poll in visible.Where(p => p.StatusAt(now) == PollStatus.Open))
        {
            open.Add(await presenter.ToModelAsync(poll));
        }

        foreach (var poll in visible.Where(p => p.StatusAt(now) == PollStatus.Closed).OrderByDescending(p => p.EndedUtc(now)))
        {
            closed.Add(await presenter.ToModelAsync(poll));
        }

        return await site.PageAsync("polls", new PageMeta { Title = site.T("polls") }, new Dictionary<string, object?>
        {
            ["open_polls"] = open,
            ["closed_polls"] = closed,
        });
    }
}
