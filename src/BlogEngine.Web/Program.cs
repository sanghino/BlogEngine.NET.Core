using BlogEngine.Core.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BlogEngineOptions>(builder.Configuration.GetSection(BlogEngineOptions.SectionName));
builder.Services.AddSingleton(sp => new BlogEnginePaths(
    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<IOptions<BlogEngineOptions>>().Value));

// Su un hosting condiviso non c'è un profilo utente in cui salvare le chiavi: senza questa
// configurazione resterebbero in memoria e ogni riavvio disconnetterebbe tutti gli utenti.
builder.Services.AddDataProtection().SetApplicationName("BlogEngine");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<BlogEnginePaths, ILoggerFactory>((options, paths, loggerFactory) =>
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.Keys), loggerFactory));

var app = builder.Build();

app.Services.GetRequiredService<BlogEnginePaths>().EnsureDataDirectories();

app.MapGet("/", () => "BlogEngine.NET su .NET 8: in costruzione.");

app.Run();

// Rende la classe Program visibile ai test di integrazione (WebApplicationFactory).
public partial class Program;
