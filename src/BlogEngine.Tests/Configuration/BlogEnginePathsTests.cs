using BlogEngine.Core.Configuration;

namespace BlogEngine.Tests.Configuration;

[TestClass]
public sealed class BlogEnginePathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "be-site", "public");

    [TestMethod]
    public void RelativeDataPath_IsResolvedAgainstContentRoot()
    {
        var paths = new BlogEnginePaths(Root, new BlogEngineOptions { DataPath = "App_Data" });

        Assert.AreEqual(Path.Combine(Root, "App_Data"), paths.DataRoot);
    }

    [TestMethod]
    public void ParentDataPath_PointsNextToContentRoot()
    {
        // Layout Aruba: /public (applicazione) e /App_Data (dati) allo stesso livello.
        var paths = new BlogEnginePaths(Root, new BlogEngineOptions { DataPath = "../App_Data" });

        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(Root)!, "App_Data"), paths.DataRoot);
    }

    [TestMethod]
    public void AbsoluteDataPath_IsUsedAsIs()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "be-data");
        var paths = new BlogEnginePaths(Root, new BlogEngineOptions { DataPath = absolute });

        Assert.AreEqual(absolute, paths.DataRoot);
    }

    [TestMethod]
    public void WritableFolders_AreAllUnderDataRoot()
    {
        var paths = new BlogEnginePaths(Root, new BlogEngineOptions { DataPath = "../App_Data" });

        foreach (var dir in new[] { paths.Files, paths.Cache, paths.Keys, paths.Logs, paths.InstalledThemes, paths.InstalledExtensions })
        {
            StringAssert.StartsWith(dir, paths.DataRoot);
        }
    }

    [TestMethod]
    public void EnsureDataDirectories_CreatesMissingFolders()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "be-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new BlogEnginePaths(Root, new BlogEngineOptions { DataPath = dataRoot });

            paths.EnsureDataDirectories();

            Assert.IsTrue(Directory.Exists(paths.Keys));
            Assert.IsTrue(Directory.Exists(paths.InstalledExtensions));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }
}
