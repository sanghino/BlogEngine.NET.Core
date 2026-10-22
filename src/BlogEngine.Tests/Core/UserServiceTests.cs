using BlogEngine.Core;
using BlogEngine.Core.Data;
using BlogEngine.Core.Security;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;
using BlogEngine.Web.Security;
using Microsoft.AspNetCore.DataProtection;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class UserServiceTests
{
    [TestMethod]
    public void PasswordHasher_VerifiesOnlyTheRightPassword()
    {
        var hash = PasswordHasher.Hash("segreta-123");

        StringAssert.StartsWith(hash, "v1.100000.");
        Assert.IsTrue(PasswordHasher.Verify(hash, "segreta-123"));
        Assert.IsFalse(PasswordHasher.Verify(hash, "Segreta-123"));
        Assert.IsFalse(PasswordHasher.Verify("jGl25bVBBBW96Qi9Te4V37Fnqchz/Eu4qB9vKrRIqRg=", "admin"), "I vecchi hash non sono accettati.");
        Assert.AreNotEqual(hash, PasswordHasher.Hash("segreta-123"), "Ogni hash ha un salt diverso.");
    }

    [TestMethod]
    public async Task CreateAndValidate_UserNamesIgnoreCase()
    {
        using var host = new TestBlogHost().AsAdmin();
        var users = host.Get<IUserService>();

        await users.CreateUserAsync("Mario", "mario@example.com", "password-1", [SystemRoles.Editors]);

        Assert.IsNotNull(await users.ValidateCredentialsAsync("mario", "password-1"));
        Assert.IsNull(await users.ValidateCredentialsAsync("mario", "password-2"));
        CollectionAssert.AreEqual(new[] { SystemRoles.Editors }, (await users.GetRolesForUserAsync("MARIO")).ToArray());
        Assert.AreEqual(TestBlogHost.StartTime.UtcDateTime, (await users.GetUserAsync("mario"))!.LastLoginTime);
    }

    [TestMethod]
    public async Task Create_ValidatesData()
    {
        using var host = new TestBlogHost().AsAdmin();
        var users = host.Get<IUserService>();
        await users.CreateUserAsync("mario", "mario@example.com", "password-1");

        var duplicate = await Assert.ThrowsExceptionAsync<BlogValidationException>(() => users.CreateUserAsync("MARIO", "altro@example.com", "password-1"));
        Assert.IsTrue(duplicate.Errors.ContainsKey("UserName"));

        var invalid = await Assert.ThrowsExceptionAsync<BlogValidationException>(() => users.CreateUserAsync("luigi rossi", "non-email", "corta"));
        CollectionAssert.AreEquivalent(new[] { "UserName", "Email", "Password" }, invalid.Errors.Keys.ToArray());

        host.AsEditor();
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => users.CreateUserAsync("luigi", "l@example.com", "password-1"));
    }

    [TestMethod]
    public async Task Register_OnlyWhenEnabledAndNeverAsAdministrator()
    {
        using var host = new TestBlogHost().AsAnonymous();
        var users = host.Get<IUserService>();
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => users.RegisterAsync("nuovo", "n@example.com", "password-1"));

        host.AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.EnableSelfRegistration = true;
        settings.SelfRegistrationInitialRole = SystemRoles.Administrators;
        await host.Settings.SaveAsync(settings);

        host.AsAnonymous();
        await users.RegisterAsync("nuovo", "n@example.com", "password-1");
        Assert.AreEqual(0, (await users.GetRolesForUserAsync("nuovo")).Count);
    }

    [TestMethod]
    public async Task Register_ByDefault_GetsMinimalRegisteredRole()
    {
        using var host = new TestBlogHost().AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.EnableSelfRegistration = true;
        await host.Settings.SaveAsync(settings);

        host.AsAnonymous();
        var users = host.Get<IUserService>();
        await users.RegisterAsync("lettore", "l@example.com", "password-1");
        CollectionAssert.AreEqual(new[] { SystemRoles.Registered }, (await users.GetRolesForUserAsync("lettore")).ToArray());

        var rights = RightsMatrix.CreateDefault().GetRightsForRole(SystemRoles.Registered);
        Assert.IsTrue(rights.Contains(Rights.CreateComments));
        Assert.IsTrue(rights.Contains(Rights.EditOwnUser));
        Assert.IsFalse(rights.Contains(Rights.AccessAdminPages), "Chi si registra non entra nell'amministrazione.");
        Assert.IsFalse(rights.Contains(Rights.CreateNewPosts), "Chi si registra non pubblica post.");
        Assert.IsTrue(SystemRoles.IsSystemRole(SystemRoles.Registered), "Il ruolo non si può eliminare.");
    }

    [TestMethod]
    public async Task TheLastAdministratorCannotBeRemoved()
    {
        using var host = new TestBlogHost().AsAdmin();
        var users = host.Get<IUserService>();
        await users.CreateUserAsync("admin", "a@example.com", "password-1", [SystemRoles.Administrators]);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => users.SetUserRolesAsync("admin", [SystemRoles.Editors]));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => users.DeleteUserAsync("admin"));

        await users.CreateUserAsync("secondo", "s@example.com", "password-1", [SystemRoles.Administrators]);
        await users.DeleteUserAsync("admin");
        Assert.IsNull(await users.GetUserAsync("admin"));
    }

    [TestMethod]
    public async Task Roles_SystemRolesAlwaysExistAndDeletedRolesLoseRights()
    {
        using var host = new TestBlogHost().AsAdmin();
        var users = host.Get<IUserService>();
        var rights = host.Get<IRightsService>();

        CollectionAssert.IsSubsetOf(new[] { SystemRoles.Administrators, SystemRoles.Editors, SystemRoles.Anonymous },
            (await users.GetRolesAsync()).Select(r => r.Name).ToArray());

        await users.CreateRoleAsync("Writers");
        await rights.SetRightsForRoleAsync("Writers", [Rights.CreateNewPosts]);
        await users.DeleteRoleAsync("writers");

        Assert.IsFalse((await users.GetRolesAsync()).Any(r => r.Name == "Writers"));
        Assert.AreEqual(0, (await rights.GetRightsForRoleAsync("Writers")).Count);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => users.DeleteRoleAsync(SystemRoles.Editors));
    }

    [TestMethod]
    public async Task ChangePassword_RequiresCurrentPassword()
    {
        using var host = new TestBlogHost().AsAdmin();
        var users = host.Get<IUserService>();
        await users.CreateUserAsync("mario", "m@example.com", "password-1");

        host.As("mario");
        Assert.IsFalse(await users.ChangePasswordAsync("sbagliata", "password-2"));
        Assert.IsTrue(await users.ChangePasswordAsync("password-1", "password-2"));
        Assert.IsNotNull(await users.ValidateCredentialsAsync("mario", "password-2"));
    }

    [TestMethod]
    public void ResetTokens_AreBoundToBlogAndCurrentPassword()
    {
        // Data Protection verifica la scadenza anche con l'orologio reale: si parte dall'ora corrente.
        var now = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(now);
        var tokens = new PasswordResetTokens(new EphemeralDataProtectionProvider(), clock);
        var blogId = Guid.NewGuid();
        var user = new BlogUser { BlogId = blogId, UserName = "mario", PasswordHash = PasswordHasher.Hash("password-1") };

        var token = tokens.Create(user);

        Assert.AreEqual("mario", tokens.GetUserName(token, blogId));
        Assert.IsTrue(tokens.IsCurrent(token, user));
        Assert.IsNull(tokens.GetUserName(token, Guid.NewGuid()), "Non vale in un altro blog.");
        Assert.IsNull(tokens.GetUserName("manomesso", blogId));

        user.PasswordHash = PasswordHasher.Hash("password-2");
        Assert.IsFalse(tokens.IsCurrent(token, user), "Dopo il cambio della password il link non vale più.");

        clock.Now = now.AddHours(3);
        Assert.IsNull(tokens.GetUserName(token, blogId), "Il link scade dopo 2 ore.");
    }
}
