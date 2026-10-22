using System.Globalization;
using BlogEngine.Core.Files;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/filemanager")]
public sealed class FileManagerController(FileStore files, IRightsService rights, IBlogContext context) : AdminApiController
{
    /// <summary>Contenuto della cartella indicata (percorso relativo ai file del blog; vuoto per la radice).</summary>
    [HttpGet("")]
    public async Task<IEnumerable<FileInstance>> Get(int take = 10, int skip = 0, string path = "", string order = "")
    {
        if (!await rights.IsAuthorizedAsync(Rights.EditOwnPosts))
        {
            throw new UnauthorizedAccessException();
        }

        var current = string.IsNullOrWhiteSpace(path) || path == "/" ? "/" : files.ToRelative(context.Blog.Id, files.Resolve(context.Blog.Id, path));
        var list = new List<FileInstance>();
        if (current != "/")
        {
            var parent = current.TrimEnd('/');
            parent = parent[..Math.Max(0, parent.LastIndexOf('/'))];
            list.Add(new FileInstance { Name = "...", FileType = FileType.Directory, FileSize = string.Empty, Created = string.Empty, FullPath = parent.Length == 0 ? "/" : parent });
        }

        foreach (var entry in files.List(context.Blog.Id, current))
        {
            list.Add(new FileInstance
            {
                Name = entry.Name,
                FullPath = entry.RelativePath,
                FileType = entry.IsDirectory ? FileType.Directory : entry.IsImage ? FileType.Image : FileType.File,
                FileSize = entry.IsDirectory ? string.Empty : FileStore.FormatSize(entry.Size),
                Created = entry.CreatedUtc.ToString("g", CultureInfo.InvariantCulture),
            });
        }

        for (var i = 0; i < list.Count; i++)
        {
            list[i].SortOrder = i;
        }

        return list;
    }

    [HttpPut("processchecked/{id}")]
    public IActionResult ProcessChecked(string id, [FromBody] List<FileInstance> items)
    {
        DemandAdministrator();
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked && i.Name != "..." && !string.IsNullOrEmpty(i.FullPath)))
            {
                files.Delete(context.Blog.Id, item.FullPath!);
            }
        }

        return Ok();
    }

    [HttpPut("addfolder/{id?}")]
    public IActionResult AddFolder([FromBody] FileInstance folder)
    {
        DemandAdministrator();
        var name = FileStore.SafeFileName(folder.Name ?? string.Empty);
        files.CreateDirectory(context.Blog.Id, $"{(folder.FullPath ?? "/").TrimEnd('/')}/{name}");
        return Ok();
    }

    private void DemandAdministrator()
    {
        if (!rights.IsAdministrator)
        {
            throw new UnauthorizedAccessException();
        }
    }
}

[Route("api/upload")]
public sealed class UploadController(
    FileStore files,
    IRightsService rights,
    IBlogContext context,
    ILinkGenerator links,
    IBlogClock clock) : AdminApiController
{
    public const long MaxAvatarSize = 1024 * 1024;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"];

    /// <summary>
    /// Caricamento di file dagli editor. action: "image" o "file" (nella cartella aaaa/mm), "filemgr" (nella
    /// cartella del file manager), "profile" (foto del profilo), "video" (nella cartella /media, per i tag
    /// [video] e [audio]). Restituisce l'indirizzo o il nome del file caricato.
    /// </summary>
    [HttpPost("")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Post(string action, string dirPath = "")
    {
        if (!Request.HasFormContentType || Request.Form.Files.Count == 0 || Request.Form.Files[0] is not { Length: > 0 } file)
        {
            return BadRequest();
        }

        var kind = action?.ToLowerInvariant() ?? string.Empty;
        var fileName = FileStore.SafeFileName(file.FileName);

        // iOS invia tutte le foto come "image.jpg" o "image.png".
        var now = await clock.ToBlogTimeAsync(clock.UtcNow);
        if (fileName is "image.jpg" or "image.png")
        {
            fileName = now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + Path.GetExtension(fileName);
        }

        if (kind is "filemgr" or "file")
        {
            kind = IsImage(fileName) ? "image" : "file";
        }

        switch (kind)
        {
            case "profile":
                return await UploadProfileImageAsync(file, fileName);
            case "image" or "file":
                if (!await rights.IsAuthorizedAsync(Rights.EditOwnPosts))
                {
                    return Unauthorized();
                }

                var directory = string.IsNullOrWhiteSpace(dirPath)
                    ? now.ToString("/yyyy/MM", CultureInfo.InvariantCulture)
                    : files.ToRelative(context.Blog.Id, files.Resolve(context.Blog.Id, dirPath));
                await using (var stream = file.OpenReadStream())
                {
                    var saved = await files.SaveAsync(context.Blog.Id, directory, fileName, stream, HttpContext.RequestAborted);
                    var savedName = Path.GetFileName(saved);
                    return kind == "image"
                        ? StatusCode(StatusCodes.Status201Created, $"{links.BlogRoot}image.axd?picture={Uri.EscapeDataString(saved)}")
                        : StatusCode(StatusCodes.Status201Created, $"{links.BlogRoot}file.axd?file={Uri.EscapeDataString(saved)}|{savedName} ({FileStore.FormatSize(file.Length)})");
                }

            case "import":
                // Importazione di un file BlogML dalle impostazioni avanzate (solo amministratori).
                if (!rights.IsAdministrator)
                {
                    return Unauthorized();
                }

                await using (var stream = file.OpenReadStream())
                {
                    var result = await HttpContext.RequestServices.GetRequiredService<Protocols.BlogMLService>().ImportAsync(stream, HttpContext.RequestAborted);
                    return Ok(result);
                }
            case "video":
                // Come in origine: il file va nella cartella dei video e l'editor inserisce [video src=nome].
                if (!await rights.IsAuthorizedAsync(Rights.EditOwnPosts))
                {
                    return Unauthorized();
                }

                await using (var stream = file.OpenReadStream())
                {
                    var saved = await files.SaveAsync(context.Blog.Id, Endpoints.FileEndpoints.MediaFolder, fileName, stream, HttpContext.RequestAborted);
                    return StatusCode(StatusCodes.Status201Created, Path.GetFileName(saved));
                }
            default:
                return BadRequest();
        }
    }

    private async Task<IActionResult> UploadProfileImageAsync(IFormFile file, string fileName)
    {
        if (!await rights.IsAuthorizedAsync(Rights.EditOwnUser) || context.UserName is not { } userName)
        {
            return Unauthorized();
        }

        if (!IsImage(fileName) || file.Length > MaxAvatarSize)
        {
            return BadRequest(new { Message = T("errorProfilePhotoTooLarge") });
        }

        // Una sola foto per utente: quelle precedenti (anche con un'altra estensione) vengono eliminate.
        var avatarName = FileStore.SafeFileName(userName) + Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var old in files.List(context.Blog.Id, "/avatars").Where(f => !f.IsDirectory && string.Equals(Path.GetFileNameWithoutExtension(f.Name), FileStore.SafeFileName(userName), StringComparison.OrdinalIgnoreCase)))
        {
            files.Delete(context.Blog.Id, old.RelativePath);
        }

        await using var stream = file.OpenReadStream();
        var saved = await files.SaveAsync(context.Blog.Id, "/avatars", avatarName, stream, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, Path.GetFileName(saved));
    }

    private static bool IsImage(string fileName) => ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
}
