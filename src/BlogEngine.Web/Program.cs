using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Endpoints;
using BlogEngine.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBlogEngineWeb(builder.Configuration);

var app = builder.Build();

app.Services.GetRequiredService<BlogEnginePaths>().EnsureDataDirectories();
var installation = app.Services.GetRequiredService<InstallationState>();
if (installation.IsInstalled)
{
    await app.Services.GetRequiredService<IDataStoreInitializer>().InitializeAsync();
}
else
{
    installation.EnsureSetupKey();
    app.Logger.LogWarning("Il blog non è ancora configurato: apri /setup e inserisci la chiave contenuta in {SetupKeyPath}.", installation.SetupKeyPath);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

app.UseStaticFiles();
app.UseMiddleware<SetupGateMiddleware>();
app.UseMiddleware<BlogContextMiddleware>();

// L'amministrazione usa indirizzi relativi ("app/..."): va aperta con la barra finale.
app.Use((http, next) =>
{
    if (http.Request.Path.Equals("/admin", StringComparison.OrdinalIgnoreCase))
    {
        http.Response.Redirect($"{http.Request.PathBase}/admin/{http.Request.QueryString}");
        return Task.CompletedTask;
    }

    return next(http);
});

app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapFileEndpoints();
app.MapAdminResources();

app.Run();

// Rende la classe Program visibile ai test di integrazione (WebApplicationFactory).
public partial class Program;
