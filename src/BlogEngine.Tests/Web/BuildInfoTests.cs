using System.Text.RegularExpressions;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Tests.Web;

/// <summary>Numero di versione della build: visibile nell'amministrazione, non nelle pagine pubbliche.</summary>
[TestClass]
public sealed class BuildInfoTests
{
    [TestMethod]
    public void Current_HasVersionNumber()
    {
        var info = BuildInfo.Current;
        StringAssert.Matches(info.Version, new Regex(@"^4\.0\.\d+$"), "Versione 4.0.N");
        Assert.IsNotNull(info.BuildDateUtc, "La data di compilazione è scritta nell'assembly.");
    }

    [TestMethod]
    public void Describe_ShowsCommitDateAndLocalChanges()
    {
        var date = new DateTime(2026, 11, 13, 2, 17, 0, DateTimeKind.Utc);
        Assert.AreEqual("4.0.486 (commit e06a49c, 13/11/2026 02:17 UTC)", new BuildInfo("4.0.486", "e06a49c", false, date).Describe());
        Assert.AreEqual("4.0.486 (commit e06a49c + modifiche locali, 13/11/2026 02:17 UTC)", new BuildInfo("4.0.486", "e06a49c", true, date).Describe());
        Assert.AreEqual("4.0.0", new BuildInfo("4.0.0", string.Empty, false, null).Describe(), "Senza git resta solo il numero.");
    }

    [TestMethod]
    public async Task Diagnostics_ShowsTheBuild_PublicPagesDoNot()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        var info = BuildInfo.Current;

        var diagnostics = await admin.GetStringAsync("/admin/diagnostics");
        StringAssert.Contains(diagnostics, "ExtraGeek-BlogEngine " + info.Version);
        if (info.Commit.Length > 0)
        {
            StringAssert.Contains(diagnostics, "commit " + info.Commit);
        }

        StringAssert.Contains(await admin.GetStringAsync("/admin/about.cshtml"), info.Version);

        using var anonymous = factory.CreateBrowser();
        var home = await anonymous.GetStringAsync("/");
        StringAssert.Contains(home, "<meta name=\"generator\" content=\"ExtraGeek-BlogEngine 4.0");
        Assert.IsFalse(home.Contains(info.Version + " ", StringComparison.Ordinal) || (info.Commit.Length > 0 && home.Contains(info.Commit, StringComparison.Ordinal)),
            "Il numero esatto della build non compare nelle pagine pubbliche.");
    }
}
