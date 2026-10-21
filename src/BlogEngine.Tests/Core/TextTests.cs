using BlogEngine.Text;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class TextTests
{
    [DataTestMethod]
    [DataRow("Hello World", "Hello-World")]
    [DataRow("  Perché l'Università è così? ", "Perche-lUniversita-e-cosi")]
    [DataRow("C# & .NET: novità", "C-NET-novita")]
    [DataRow("a -- b", "a-b")]
    [DataRow("“Virgolette” tipografiche", "Virgolette-tipografiche")]
    [DataRow("Trattino – lungo", "Trattino-lungo")]
    [DataRow("<script>alert(1)</script>", "scriptalert(1)script")]
    [DataRow("", "")]
    public void Slug_Create_RemovesCharactersNotValidInUrls(string input, string expected)
    {
        Assert.AreEqual(expected, Slug.Create(input));
    }

    [TestMethod]
    public void Slug_AreEquivalent_IgnoresCaseAndPunctuation()
    {
        Assert.IsTrue(Slug.AreEquivalent("ASP.NET Core", "aspnet-core"));
        Assert.IsFalse(Slug.AreEquivalent("uno", "due"));
    }

    [TestMethod]
    public void HtmlText_Strip_RemovesTagsButKeepsEntitiesEncoded()
    {
        Assert.AreEqual("ciao &lt;b&gt; mondo", HtmlText.Strip("<p>ciao &lt;b&gt; <em>mondo</em></p>"));
    }

    [TestMethod]
    public void HtmlText_FirstImageSource_FindsFirstImage()
    {
        Assert.AreEqual("/a.png", HtmlText.FirstImageSource("<p>x</p><img class='x' src='/a.png'><img src=\"/b.png\">"));
        Assert.IsNull(HtmlText.FirstImageSource("<p>nessuna immagine</p>"));
    }
}
