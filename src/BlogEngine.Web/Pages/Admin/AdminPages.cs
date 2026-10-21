using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Web.Pages.Admin
{
    public sealed class IndexModel() : AdminPageModel(Rights.AccessAdminPages)
    {
        public void OnGet()
        {
        }
    }

    public sealed class AboutModel(InstallationState installation) : AdminPageModel(Rights.AccessAdminPages)
    {
        public string DataProvider => installation.DataProvider.ToString();

        public void OnGet()
        {
        }
    }
}

namespace BlogEngine.Web.Pages.Admin.Editor
{
    public sealed class EditPostModel() : AdminPageModel(Rights.EditOwnPosts)
    {
        public void OnGet()
        {
        }
    }

    public sealed class EditPageModel() : AdminPageModel(Rights.EditOwnPages)
    {
        public void OnGet()
        {
        }
    }

    /// <summary>Finestra del file manager aperta dal plugin di TinyMCE.</summary>
    public sealed class FileManagerModel(ILinkGenerator links) : AdminPageModel(Rights.EditOwnPosts)
    {
        public string ImageHandler => $"{links.BlogRoot}image.axd?picture=";

        public void OnGet()
        {
        }
    }
}
