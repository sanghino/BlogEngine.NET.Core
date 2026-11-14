using System.Globalization;
using BlogEngine.Core;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/users")]
public sealed class UsersController(
    IUserService users,
    IProfileService profiles,
    IRightsService rights,
    IBlogContext context) : AdminApiController
{
    /// <summary>Elenco degli utenti; con il parametro "id" (usato dalla scheda utente) restituisce solo quell'utente.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(int take = 10, int skip = 0, string filter = "1 == 1", string order = "UserName", string? id = null)
    {
        if (!string.IsNullOrEmpty(id))
        {
            return await Get(id);
        }

        await DemandAsync(Rights.AccessAdminPages);
        var items = new List<BlogUserDto>();
        foreach (var user in await users.GetUsersAsync())
        {
            items.Add(await ToDtoAsync(user.UserName, user.Email));
        }

        return Ok(ApiQuery.Apply(items, filter, string.IsNullOrEmpty(order) ? "UserName" : order, take, skip));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        await DemandAsync(Rights.AccessAdminPages);
        return await users.GetUserAsync(id) is { } user ? Ok(await ToDtoAsync(user.UserName, user.Email)) : NotFound();
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] BlogUserDto item)
    {
        if (string.IsNullOrWhiteSpace(item.UserName) || string.IsNullOrWhiteSpace(item.Email) || string.IsNullOrEmpty(item.Password))
        {
            return BadRequest(new { Message = T("errorMissingRequiredFields") });
        }

        BlogEngine.Core.Data.BlogUser created;
        try
        {
            created = await users.CreateUserAsync(item.UserName, item.Email, item.Password, CheckedRoles(item), item.AllowSharedEmail);
        }
        catch (BlogValidationException ex) when (CanConfirmSharedEmail(ex, item))
        {
            return await SharedEmailConflictAsync(item, null);
        }

        item.UserName = created.UserName;
        await SaveProfileAsync(item, created.Email);
        item.Password = string.Empty;
        item.Warning = await SharedEmailWarningAsync(created.Email, created.UserName);
        return StatusCode(StatusCodes.Status201Created, item);
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] BlogUserDto item)
    {
        if (string.IsNullOrWhiteSpace(item.UserName) || string.IsNullOrWhiteSpace(item.Email))
        {
            return BadRequest(new { Message = T("errorMissingRequiredFields") });
        }

        if (await users.GetUserAsync(item.UserName) is not { } user)
        {
            return NotFound();
        }

        try
        {
            await users.UpdateEmailAsync(user.UserName, item.Email, item.AllowSharedEmail);
        }
        catch (BlogValidationException ex) when (CanConfirmSharedEmail(ex, item))
        {
            return await SharedEmailConflictAsync(item, user.UserName);
        }

        // Cambio password dalla scheda utente: serve la password attuale se si modifica il proprio account.
        if (!string.IsNullOrEmpty(item.Password))
        {
            if (IsSelf(user.UserName) && !string.IsNullOrEmpty(item.OldPassword))
            {
                if (!await users.ChangePasswordAsync(item.OldPassword, item.Password))
                {
                    return BadRequest(new { Message = T("errorCurrentPasswordWrong") });
                }
            }
            else
            {
                await users.SetPasswordAsync(user.UserName, item.Password);
            }
        }

        await SaveProfileAsync(item, item.Email);
        // I ruoli si toccano solo se cambiano davvero: salvando il proprio profilo l'amministrazione rimanda anche
        // i ruoli, e un utente senza il permesso di modificarli riceverebbe altrimenti un errore.
        if (item.Roles is not null)
        {
            var wanted = CheckedRoles(item).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var current = (await users.GetRolesForUserAsync(user.UserName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!wanted.SetEquals(current))
            {
                await users.SetUserRolesAsync(user.UserName, wanted);
            }
        }

        return Ok(new { Warning = await SharedEmailWarningAsync(item.Email, user.UserName) });
    }

    [HttpPut("saveprofile/{id?}")]
    public async Task<IActionResult> SaveProfile([FromBody] BlogUserDto item)
    {
        if (string.IsNullOrWhiteSpace(item.UserName))
        {
            return BadRequest();
        }

        await SaveProfileAsync(item, item.Email ?? (await users.GetUserAsync(item.UserName))?.Email ?? string.Empty);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await RemoveAsync(id);
        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<BlogUserDto> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked && !string.IsNullOrEmpty(i.UserName)))
            {
                await RemoveAsync(item.UserName!);
            }
        }

        return Ok();
    }

    private async Task RemoveAsync(string userName)
    {
        await users.DeleteUserAsync(userName);
        if (await profiles.GetAsync(userName) is not null)
        {
            await profiles.DeleteAsync(userName);
        }
    }

    private async Task<BlogUserDto> ToDtoAsync(string userName, string email)
    {
        var allRoles = await users.GetRolesAsync();
        var userRoles = await users.GetRolesForUserAsync(userName);
        var profile = await profiles.GetAsync(userName);
        return new BlogUserDto
        {
            UserName = userName,
            Email = email,
            Profile = ToProfileDto(profile, userName, email),
            Roles = allRoles
                .Where(r => userRoles.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                .Select(r => new RoleItem { RoleName = r.Name, IsSystemRole = SystemRoles.IsSystemRole(r.Name), IsChecked = true })
                .ToList(),
        };
    }

    private static ProfileDto ToProfileDto(AuthorProfile? profile, string userName, string email) => profile is null
        ? new ProfileDto { DisplayName = userName, FirstName = userName, EmailAddress = email, Birthday = string.Empty, PhotoUrl = string.Empty, Private = true }
        : new ProfileDto
        {
            DisplayName = profile.DisplayName,
            FirstName = profile.FirstName,
            MiddleName = profile.MiddleName,
            LastName = profile.LastName,
            EmailAddress = profile.EmailAddress,
            Birthday = profile.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            PhotoUrl = profile.PhotoUrl.Replace("\"", string.Empty),
            Private = profile.Private,
            PhoneMobile = profile.PhoneMobile,
            PhoneMain = profile.PhoneMain,
            PhoneFax = profile.PhoneFax,
            CityTown = profile.CityTown,
            RegionState = profile.RegionState,
            Country = profile.Country,
            AboutMe = profile.AboutMe,
        };

    private async Task SaveProfileAsync(BlogUserDto item, string email)
    {
        if (item.Profile is not { } p || string.IsNullOrWhiteSpace(item.UserName))
        {
            return;
        }

        await profiles.SaveAsync(new AuthorProfile
        {
            UserName = item.UserName,
            DisplayName = p.DisplayName ?? string.Empty,
            FirstName = p.FirstName ?? string.Empty,
            MiddleName = p.MiddleName ?? string.Empty,
            LastName = p.LastName ?? string.Empty,
            EmailAddress = email,
            Birthday = DateTime.TryParse(p.Birthday, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var birthday) ? birthday : null,
            PhotoUrl = (p.PhotoUrl ?? string.Empty).Replace("\"", string.Empty),
            Private = p.Private,
            PhoneMobile = p.PhoneMobile ?? string.Empty,
            PhoneMain = p.PhoneMain ?? string.Empty,
            PhoneFax = p.PhoneFax ?? string.Empty,
            CityTown = p.CityTown ?? string.Empty,
            RegionState = p.RegionState ?? string.Empty,
            Country = p.Country ?? string.Empty,
            AboutMe = p.AboutMe ?? string.Empty,
        });
    }

    /// <summary>
    /// Se l'unico problema è l'email già usata da un altro utente e chi salva è un amministratore, invece dell'errore
    /// restituisce 409 con la domanda di conferma: l'amministrazione la mostra e, se confermata, salva di nuovo con
    /// <see cref="BlogUserDto.AllowSharedEmail"/>. Negli altri casi resta l'errore normale.
    /// </summary>
    private bool CanConfirmSharedEmail(BlogValidationException ex, BlogUserDto item) =>
        !item.AllowSharedEmail && rights.IsAdministrator && ex.Errors.Count == 1
        && ex.Errors.TryGetValue("Email", out var error) && error == Messages.EmailInUse;

    private async Task<IActionResult> SharedEmailConflictAsync(BlogUserDto item, string? ownerUserName)
    {
        var email = item.Email!.Trim();
        var other = await OtherUserWithEmailAsync(email, ownerUserName ?? item.UserName);
        return Conflict(new { Message = T(Messages.Format("confirmSharedEmail", email, other ?? string.Empty)), CanShareEmail = true, SharedWith = other });
    }

    /// <summary>Avviso dopo il salvataggio se l'email è anche di un altro utente.</summary>
    private async Task<string?> SharedEmailWarningAsync(string? email, string userName) =>
        string.IsNullOrWhiteSpace(email) || await OtherUserWithEmailAsync(email.Trim(), userName) is not { } other
            ? null
            : T(Messages.Format("warningSharedEmail", other));

    private async Task<string?> OtherUserWithEmailAsync(string email, string? userName) =>
        (await users.GetUsersAsync())
            .Where(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.UserName)
            .FirstOrDefault();

    private static List<string> CheckedRoles(BlogUserDto item) =>
        (item.Roles ?? []).Where(r => r.IsChecked && !string.IsNullOrWhiteSpace(r.RoleName)).Select(r => r.RoleName!).ToList();

    private bool IsSelf(string userName) => string.Equals(context.UserName, userName, StringComparison.OrdinalIgnoreCase);

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}
