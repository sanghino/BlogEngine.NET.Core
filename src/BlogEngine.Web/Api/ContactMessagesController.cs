using System.Globalization;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Public;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Messaggi del modulo di contatto bloccati dal filtro antispam (Contenuto → Messaggi bloccati).</summary>
[Route("api/contactmessages")]
public sealed class ContactMessagesController(
    ContactMessageService messages,
    ContactFilterSettingsService settings,
    ILinkReputationService linkReputation,
    IRightsService rights,
    IBlogClock clock,
    ILabels labels) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<ContactMessageItem>> Get()
    {
        await DemandAsync();
        var items = new List<ContactMessageItem>();
        foreach (var message in await messages.GetAllAsync())
        {
            items.Add(new ContactMessageItem
            {
                Id = message.Id,
                Date = await Local(message.DateCreated),
                Name = message.Name,
                Email = message.Email,
                Subject = message.Subject,
                Text = message.Text,
                IP = message.IP,
                UserAgent = message.UserAgent,
                BlockedBy = message.BlockedBy,
                Reason = message.Reason,
                Released = message.Status == ContactMessageStatus.Released,
                DateReleased = message.DateReleased is { } released ? await Local(released) : string.Empty,
                ReleasedBy = message.ReleasedBy,
                AttachmentName = message.AttachmentName,
                AttachmentSize = message.AttachmentData?.Length ?? 0,
            });
        }

        return items;
    }

    /// <summary>Recapita il messaggio all'indirizzo del blog.</summary>
    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id)
    {
        await DemandAsync();
        try
        {
            return await messages.ReleaseAsync(id) is { } error ? BadRequest(new { message = error }) : Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await DemandAsync();
        return await messages.DeleteAsync(id) ? Ok() : NotFound();
    }

    /// <summary>Elimina tutti i messaggi ancora bloccati.</summary>
    [HttpDelete("")]
    public async Task<IActionResult> DeleteBlocked()
    {
        await DemandAsync();
        return Ok(new { deleted = await messages.DeleteBlockedAsync() });
    }

    // ---------------------------------------------------------------- regole del filtro

    /// <summary>Controlli automatici (attivi o no) e regole personalizzate.</summary>
    [HttpGet("settings")]
    public async Task<ContactFilterSettings> GetSettings()
    {
        await DemandAsync();
        return await settings.GetAsync();
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] ContactFilterSettings value)
    {
        await DemandAsync();
        if (value is null || value.MinSeconds is < 0 or > 600 || value.MaxLinks is < 0 or > 100)
        {
            return BadRequest(new { message = labels["contactRuleInvalid"] });
        }

        value.Rules = value.Rules.Where(r => r.Value.Trim().Length > 0).ToList();
        if (value.Rules.FirstOrDefault(r => r.Validate() is not null) is { } invalid)
        {
            return BadRequest(new { message = $"{labels["contactRuleInvalid"]}: \"{invalid.Value}\"" });
        }

        await settings.SaveAsync(value);
        return Ok(await settings.GetAsync());
    }

    /// <summary>Dal messaggio bloccato: mette in lista nera l'email, il dominio dell'email o l'IP del mittente.</summary>
    [HttpPost("{id:guid}/blacklist/{field}")]
    public async Task<IActionResult> Blacklist(Guid id, ContactRuleField field)
    {
        await DemandAsync();
        if ((await messages.GetAllAsync()).FirstOrDefault(m => m.Id == id) is not { } message)
        {
            return NotFound();
        }

        var value = field switch
        {
            ContactRuleField.Email => message.Email,
            ContactRuleField.EmailDomain => ContactFilterRule.DomainOf(message.Email),
            ContactRuleField.IP => message.IP,
            _ => string.Empty,
        };
        if (value.Length == 0)
        {
            return BadRequest(new { message = labels["contactRuleInvalid"] });
        }

        var added = await settings.AddRuleAsync(new ContactFilterRule
        {
            Field = field,
            Operator = ContactRuleOperator.Equals,
            Value = value,
            Action = ContactRuleAction.Block,
            Note = message.Subject.Length > 60 ? message.Subject[..60] : message.Subject,
        });
        return Ok(new { added, value });
    }

    /// <summary>Prova le liste dei domini segnalati con i loro domini di test.</summary>
    [HttpGet("linktest")]
    public async Task<IReadOnlyList<LinkReputationStatus>> LinkTest()
    {
        await DemandAsync();
        return await linkReputation.TestAsync(HttpContext.RequestAborted);
    }

    private async Task<string> Local(DateTime utc) =>
        (await clock.ToBlogTimeAsync(utc)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ModerateComments))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

public sealed record ContactMessageItem
{
    public Guid Id { get; init; }

    public string Date { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string IP { get; init; } = string.Empty;

    public string UserAgent { get; init; } = string.Empty;

    public string BlockedBy { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool Released { get; init; }

    public string DateReleased { get; init; } = string.Empty;

    public string ReleasedBy { get; init; } = string.Empty;

    public string AttachmentName { get; init; } = string.Empty;

    public int AttachmentSize { get; init; }
}
