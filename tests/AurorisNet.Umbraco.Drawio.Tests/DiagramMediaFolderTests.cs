using Drawio;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace AurorisNet.Umbraco.Drawio.Tests;

public sealed partial class SiteTests
{
    [Fact]
    public void The_upload_folder_can_move_and_be_renamed_but_its_branch_cannot_be_removed()
    {
        using var scope = Site.Services.GetRequiredService<ICoreScopeProvider>().CreateCoreScope();
        var media = Site.Services.GetRequiredService<IMediaService>();
        var parent = media.CreateMedia("Site assets", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        Assert.True(media.Save(parent).Success);
        var folder = Assert.IsAssignableFrom<IMedia>(media.GetById(DiagramMediaFolder.Key));
        folder.Name = "Site plans";
        Assert.True(media.Save(folder).Success);
        Assert.True(media.Move(folder, parent.Id).Success);
        var child = media.CreateMedia("Plans for review", folder.Id, Constants.Conventions.MediaTypes.Folder);
        Assert.True(media.Save(child).Success);
        Assert.True(media.MoveToRecycleBin(child).Success);
        Assert.True(media.Delete(child).Success);

        Assert.False(media.MoveToRecycleBin(folder).Success);
        Assert.False(media.Delete(folder).Success);
        Assert.False(media.MoveToRecycleBin(parent).Success);
        Assert.False(media.Delete(parent).Success);
        var saved = Assert.IsAssignableFrom<IMedia>(media.GetById(DiagramMediaFolder.Key));
        Assert.Equal("Site plans", saved.Name);
        Assert.Equal(parent.Id, saved.ParentId);
        Assert.False(saved.Trashed);
        Assert.True(media.Move(saved, Constants.System.Root).Success);
        Assert.True(media.Delete(parent).Success);
    }

    [Fact]
    public void A_recycled_upload_folder_survives_emptying_the_bin_and_can_be_restored()
    {
        using var scope = Site.Services.GetRequiredService<ICoreScopeProvider>().CreateCoreScope();
        var media = Site.Services.GetRequiredService<IMediaService>();
        var folder = Assert.IsAssignableFrom<IMedia>(media.GetById(DiagramMediaFolder.Key));
        // Simulate a folder recycled while the package was disabled.
        using (scope.Notifications.Suppress())
            Assert.True(media.MoveToRecycleBin(folder).Success);

        Assert.False(media.EmptyRecycleBin().Success);
        var recycled = Assert.IsAssignableFrom<IMedia>(media.GetById(DiagramMediaFolder.Key));
        Assert.True(recycled.Trashed);
        Assert.True(media.Move(recycled, Constants.System.Root).Success);
    }

    [Fact]
    public async Task Startup_does_not_recreate_a_missing_upload_folder_or_adopt_one_with_the_same_name()
    {
        using var scope = Site.Services.GetRequiredService<ICoreScopeProvider>().CreateCoreScope();
        var media = Site.Services.GetRequiredService<IMediaService>();
        var folder = Assert.IsAssignableFrom<IMedia>(media.GetById(DiagramMediaFolder.Key));
        using (scope.Notifications.Suppress())
            Assert.True(media.Delete(folder).Success);
        var other = media.CreateMedia("diagrams", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        Assert.True(media.Save(other).Success);
        Assert.Equal("diagrams", other.Name);

        var install = ActivatorUtilities.CreateInstance<DrawioInstallation>(Site.Services);
        await install.HandleAsync(new UmbracoApplicationStartingNotification(RuntimeLevel.Run, true), Cancel);
        Assert.DoesNotContain(media.GetRootMedia(), item => item.Key == DiagramMediaFolder.Key);
        Assert.True(media.Delete(other).Success);
    }
}
