using System.Reflection;
using System.Text.RegularExpressions;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Group = BlogEngine.Web.Api.Models.Group;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/roles")]
public sealed partial class RolesController(IUserService users, IRightsService rights) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<RoleItem>> Get(int take = 10, int skip = 0, string filter = "", string order = "")
    {
        await DemandAsync(Rights.ViewRoles);
        var roles = (await users.GetRolesAsync()).Select(r => new RoleItem { RoleName = r.Name, IsSystemRole = SystemRoles.IsSystemRole(r.Name) }).ToList();
        return ApiQuery.Apply(roles, filter, string.IsNullOrEmpty(order) ? "RoleName" : order, take, skip);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        await DemandAsync(Rights.ViewRoles);
        var role = (await users.GetRolesAsync()).FirstOrDefault(r => string.Equals(r.Name, id, StringComparison.OrdinalIgnoreCase));
        return role is null ? NotFound() : Ok(new RoleItem { RoleName = role.Name, IsSystemRole = SystemRoles.IsSystemRole(role.Name) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] RoleItem role)
    {
        await users.CreateRoleAsync(role.RoleName ?? string.Empty);
        return StatusCode(StatusCodes.Status201Created, new RoleItem { RoleName = role.RoleName?.Trim(), IsSystemRole = false });
    }

    /// <summary>Elimina i ruoli indicati (PUT con l'elenco, come nella versione originale).</summary>
    [HttpPut("")]
    public async Task<IActionResult> Put([FromBody] List<RoleItem> items)
    {
        if (items is not { Count: > 0 })
        {
            return NotFound();
        }

        foreach (var item in items.Where(i => !string.IsNullOrEmpty(i.RoleName)))
        {
            await users.DeleteRoleAsync(item.RoleName!);
        }

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await users.DeleteRoleAsync(id);
        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<RoleItem> items)
    {
        if (items is not { Count: > 0 })
        {
            return NotFound();
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked && !string.IsNullOrEmpty(i.RoleName)))
            {
                await users.DeleteRoleAsync(item.RoleName!);
            }
        }

        return Ok();
    }

    /// <summary>Permessi del ruolo raggruppati per categoria, con quelli assegnati selezionati.</summary>
    [HttpGet("getrights/{id}")]
    public async Task<IActionResult> GetRights(string id)
    {
        await DemandAsync(Rights.ViewRoles);
        var granted = await rights.GetRightsForRoleAsync(id);
        var groups = new List<Group>();
        foreach (var field in typeof(Rights).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var right = (Rights)field.GetValue(null)!;
            if (right == Rights.None)
            {
                continue;
            }

            var category = (field.GetCustomAttribute<RightDetailsAttribute>()?.Category ?? RightCategory.General).ToString();
            var group = groups.FirstOrDefault(g => g.Title == category);
            if (group is null)
            {
                groups.Add(group = new Group { Title = category });
            }

            group.Permissions.Add(new Permission { Id = right.ToString(), Title = DisplayName(right.ToString()), IsChecked = granted.Contains(right) });
        }

        return Ok(groups);
    }

    /// <summary>Ruoli assegnabili (escluso Anonymous) con quelli dell'utente selezionati.</summary>
    [HttpGet("getuserroles/{id}")]
    public async Task<IActionResult> GetUserRoles(string id)
    {
        await DemandAsync(Rights.ViewRoles);
        var userRoles = await users.GetRolesForUserAsync(id);
        var roles = (await users.GetRolesAsync())
            .Where(r => !string.Equals(r.Name, SystemRoles.Anonymous, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new RoleItem
            {
                RoleName = r.Name,
                IsSystemRole = SystemRoles.IsSystemRole(r.Name),
                IsChecked = userRoles.Contains(r.Name, StringComparer.OrdinalIgnoreCase),
            });
        return Ok(roles);
    }

    [HttpPut("saverights/{id}")]
    public async Task<IActionResult> SaveRights(string id, [FromBody] List<Group> groups)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { Message = "Invalid role name" });
        }

        var granted = (groups ?? [])
            .SelectMany(g => g.Permissions)
            .Where(p => p.IsChecked && Enum.TryParse<Rights>(p.Id, out _))
            .Select(p => Enum.Parse<Rights>(p.Id!))
            .ToList();

        await rights.SetRightsForRoleAsync(id, granted);
        return Ok();
    }

    /// <summary>"EditOwnPosts" → "Edit Own Posts" (porting di Utils.FormatIdentifierForDisplay).</summary>
    private static string DisplayName(string identifier) => WordBoundary().Replace(identifier, " $1").Trim();

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordBoundary();
}
