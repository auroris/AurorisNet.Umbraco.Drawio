using System.Text.Json;
using Drawio;
using Drawio.Compatibility;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace AurorisNet.Umbraco.Drawio.Tests;

public sealed partial class SiteTests
{
    private sealed class SaveTrackingApi(IServiceProvider services) : UmbracoApi(services)
    {
        public override int MajorVersion { get; } = services.GetRequiredService<IUmbracoApi>().MajorVersion;
        internal int WriteScopes { get; private set; }
        internal bool ScannedHistory { get; private set; }
        internal bool FailRelations { get; set; }
        // Tracking logs its own failures, so record a history scan here.
        protected override IEnumerable<IContent> GetVersions(int contentId)
        {
            ScannedHistory = true;
            return [];
        }
        public override T WithTreeWriteScope<T>(Func<T> work)
        {
            WriteScopes++;
            return base.WithTreeWriteScope(work);
        }
        public override void AddRetentionRelation(int contentId, int mediaId)
        {
            if (FailRelations) throw new InvalidOperationException("The relation could not be stored.");
            base.AddRetentionRelation(contentId, mediaId);
        }
    }

    [Fact]
    public async Task Saving_tracks_only_the_saved_version_and_skips_tree_locks_for_ordinary_uploads()
    {
        var api = new SaveTrackingApi(Site.Services);
        var retention = ActivatorUtilities.CreateInstance<DiagramMediaRetention>(Site.Services, api);
        var page = await DiagramPage("trackCurrentOnly");
        var content = Site.Services.GetRequiredService<IContentService>();
        Pick(page, DiagramImage(owned: false));
        Assert.True(content.Save(page).Success);
        retention.Track([page]);
        Assert.Equal(0, api.WriteScopes);
        Pick(page, DiagramImage());
        Assert.True(content.Save(page).Success);
        retention.Track([page]);
        Assert.Equal(1, api.WriteScopes);
        Assert.False(api.ScannedHistory);
    }

    /// <summary>Tracking runs once the save has committed, so its failure must not turn a saved page into an error.</summary>
    [Fact]
    public async Task A_tracking_failure_leaves_the_save_standing_and_the_next_save_records_the_diagram()
    {
        var api = new SaveTrackingApi(Site.Services);
        var retention = ActivatorUtilities.CreateInstance<DiagramMediaRetention>(Site.Services, api);
        var page = await DiagramPage("trackFailure");
        var image = DiagramImage();
        Pick(page, image);
        Assert.True(Site.Services.GetRequiredService<IContentService>().Save(page).Success);
        // The site's own handler has recorded the relation. Remove it, so this tracker has one to add.
        foreach (var relation in api.GetRetentionRelations(page.Id)) api.DeleteRetentionRelation(relation.Id);
        api.FailRelations = true;
        retention.Track([page]);
        Assert.Empty(api.GetRetentionRelations(page.Id));
        api.FailRelations = false;
        retention.Track([page]);
        Assert.Equal(image.Id, Assert.Single(api.GetRetentionRelations(page.Id)).MediaId);
    }

    [Fact]
    public async Task Automatic_cleanup_preserves_protected_history_then_deletes_the_unused_media_and_file()
    {
        var page = await DiagramPage("retentionHistory");
        var content = Site.Services.GetRequiredService<IContentService>();
        var versions = Site.Services.GetRequiredService<IContentVersionService>();
        var a = DiagramImage(); var b = DiagramImage();
        Pick(page, a); Assert.True(content.Save(page).Success); Assert.True(content.Publish(page, ["*"]).Success);
        Pick(page, b); Assert.True(content.Save(page).Success); Assert.True(content.Publish(page, ["*"]).Success);
        AssertMediaExists(a);
        AssertMediaExists(b);
        var historic = content.GetVersions(page.Id).Where(v => v.GetValue<string>("diagrams")?.Contains(a.Key.ToString()) == true).ToArray();
        Assert.NotEmpty(historic);
        foreach (var version in historic)
            Assert.True((await versions.SetPreventCleanupAsync(version.VersionId.ToGuid(), true, Constants.Security.SuperUserKey)).Success);
        versions.PerformContentVersionCleanup(DateTime.UtcNow.AddDays(200));
        AssertMediaExists(a);
        foreach (var version in historic)
            Assert.True((await versions.SetPreventCleanupAsync(version.VersionId.ToGuid(), false, Constants.Security.SuperUserKey)).Success);
        versions.PerformContentVersionCleanup(DateTime.UtcNow.AddDays(200));
        AssertMediaDeleted(a);
        AssertMediaExists(b);
    }

    [Fact]
    public async Task A_shared_diagram_survives_until_the_last_page_and_its_history_are_deleted()
    {
        var first = await DiagramPage("retentionSharedA");
        var second = await DiagramPage("retentionSharedB");
        var content = Site.Services.GetRequiredService<IContentService>();
        var shared = DiagramImage(); var replacement = DiagramImage();
        Pick(first, shared); content.Save(first); content.Publish(first, ["*"]);
        Pick(second, shared); content.Save(second); content.Publish(second, ["*"]);
        Pick(second, replacement); content.Save(second); content.Publish(second, ["*"]);
        Assert.True(content.Delete(first).Success);
        AssertMediaExists(shared); // The second page's historical version is still restorable.
        Assert.True(content.Delete(second).Success);
        AssertMediaDeleted(shared);
        AssertMediaDeleted(replacement);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_separate_media_relation_keeps_the_diagram(bool diagramIsParent)
    {
        var page = await DiagramPage(diagramIsParent ? "relatedParent" : "relatedChild");
        var diagram = DiagramImage();
        var other = DiagramImage(owned: false);
        var relations = Site.Services.GetRequiredService<IRelationService>();
        var relationType = new RelationType("Related diagrams " + page.Name, page.Name!, true,
            Constants.ObjectTypes.Media, Constants.ObjectTypes.Media, false);
        relations.Save(relationType);
        var relation = relations.Relate(diagramIsParent ? diagram.Id : other.Id, diagramIsParent ? other.Id : diagram.Id, relationType);
        var content = Site.Services.GetRequiredService<IContentService>();
        Pick(page, diagram);
        Assert.True(content.Save(page).Success);
        Assert.True(content.Delete(page).Success);
        AssertMediaExists(diagram);
        Assert.NotNull(relations.GetById(relation.Id));
    }

    [Fact]
    public async Task Moving_to_the_recycle_bin_and_restoring_does_not_delete_diagrams()
    {
        var page = await DiagramPage("retentionRecycle");
        var content = Site.Services.GetRequiredService<IContentService>();
        var image = DiagramImage(); Pick(page, image); content.Save(page);
        Assert.True(content.MoveToRecycleBin(page).Success);
        AssertMediaExists(image);
        Assert.True(content.Move(page, Constants.System.Root).Success);
        AssertMediaExists(image);
        Assert.True(content.MoveToRecycleBin(page).Success);
        Assert.True(content.Delete(page).Success);
        AssertMediaDeleted(image);
    }

    [Fact]
    public async Task A_rolled_back_deletion_keeps_the_media()
    {
        var page = await DiagramPage("retentionRollback");
        var content = Site.Services.GetRequiredService<IContentService>();
        var image = DiagramImage(); Pick(page, image); content.Save(page);
        using (Site.Services.GetRequiredService<ICoreScopeProvider>().CreateCoreScope())
            Assert.True(content.Delete(page).Success);
        Assert.NotNull(content.GetById(page.Id));
        AssertMediaExists(image);
    }

    [Fact]
    public async Task Deleting_a_branch_cleans_diagrams_from_its_children_but_keeps_ordinary_uploads()
    {
        var parent = await DiagramPage("retentionBranch");
        var content = Site.Services.GetRequiredService<IContentService>();
        content.Save(parent);
        var child = content.Create("Child", parent.Id, parent.ContentType.Alias);
        var diagram = DiagramImage(); var ordinary = DiagramImage(owned: false);
        Pick(child, diagram); content.Save(child); content.Publish(child, ["*"]);
        Pick(child, ordinary); content.Save(child);
        Assert.True(content.Delete(parent).Success);
        AssertMediaDeleted(diagram);
        AssertMediaExists(ordinary);
    }

    [Theory]
    [InlineData(SupersededImageHandling.Keep)]
    [InlineData(SupersededImageHandling.RecycleBin)]
    public async Task The_media_adapter_honors_keep_and_recycle_options(SupersededImageHandling handling)
    {
        var settings = Site.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<DrawioOptions>>().CurrentValue;
        var previous = settings.SupersededImages;
        try
        {
            settings.SupersededImages = handling;
            var page = await DiagramPage("retention" + handling);
            var content = Site.Services.GetRequiredService<IContentService>();
            var image = DiagramImage(); Pick(page, image); Assert.True(content.Save(page).Success);
            Assert.True(content.Delete(page).Success);
            var retained = Assert.IsAssignableFrom<IMedia>(Site.Services.GetRequiredService<IMediaService>().GetById(image.Key));
            Assert.Equal(handling == SupersededImageHandling.RecycleBin, retained.Trashed);
            // Newer CMS versions hide recycled files; restoring must recover the original bytes on both versions.
            if (retained.Trashed)
                Assert.True(Site.Services.GetRequiredService<IMediaService>().Move(retained, Constants.System.Root).Success);
            AssertMediaExists(retained);
            using var stream = Site.Services.GetRequiredService<MediaFileManager>().FileSystem.OpenFile(ImagePath(retained));
            using var restored = new MemoryStream();
            await stream.CopyToAsync(restored, Cancel);
            Assert.Equal(Png, restored.ToArray());
        }
        finally { settings.SupersededImages = previous; }
    }

    private async Task<IContent> DiagramPage(string alias)
    {
        var services = Site.Services;
        var strings = services.GetRequiredService<IShortStringHelper>();
        var editors = services.GetRequiredService<PropertyEditorCollection>();
        var dataType = new DataType(editors[Constants.PropertyEditors.Aliases.BlockList], services.GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = alias + " blocks", EditorUiAlias = "Umb.PropertyEditorUi.BlockList",
            ConfigurationData = new Dictionary<string, object>
            {
                ["blocks"] = new[] { new { contentElementTypeKey = DrawioInstallation.ElementTypeKey } }
            }
        };
        Assert.True((await services.GetRequiredService<IDataTypeService>().CreateAsync(dataType, Constants.Security.SuperUserKey)).Success);
        var type = new ContentType(strings, Constants.System.Root) { Alias = alias, Name = alias, AllowedAsRoot = true };
        type.AddPropertyType(new PropertyType(strings, dataType, "diagrams") { Name = "Diagrams" });
        Assert.True((await services.GetRequiredService<IContentTypeService>().CreateAsync(type, Constants.Security.SuperUserKey)).Success);
        // Build the page from the type itself. Umbraco 18.0.0 sometimes fails to find a new type by its alias this soon.
        return new Content(alias, Constants.System.Root, type);
    }

    private IMedia DiagramImage(bool owned = true, string? map = null)
    {
        var service = Site.Services.GetRequiredService<IMediaService>();
        var image = service.CreateMedia("Diagram", Constants.System.Root, owned ? DrawioInstallation.MediaTypeAlias : Constants.Conventions.MediaTypes.Image);
        var name = owned ? $"diagram-{image.Key:N}.png" : "photo.png";
        image.SetValue(Site.Services.GetRequiredService<MediaFileManager>(), Site.Services.GetRequiredService<MediaUrlGeneratorCollection>(),
            Site.Services.GetRequiredService<IShortStringHelper>(), Site.Services.GetRequiredService<IContentTypeBaseServiceProvider>(),
            Constants.Conventions.Media.File, name, new MemoryStream(Png));
        if (owned)
        {
            void SetFile(string alias, string extension, string value) =>
                image.SetValue(Site.Services.GetRequiredService<MediaFileManager>(), Site.Services.GetRequiredService<MediaUrlGeneratorCollection>(),
                    Site.Services.GetRequiredService<IShortStringHelper>(), Site.Services.GetRequiredService<IContentTypeBaseServiceProvider>(),
                    alias, $"diagram-{image.Key:N}.{extension}", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(value)));
            SetFile("drawioSource", "drawio", "<mxfile><diagram/></mxfile>");
            SetFile("drawioSvg", "svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 20"/>""");
            image.SetValue("drawioMap", map);
        }
        Assert.True(service.Save(image).Success);
        return image;
    }

    private static void Pick(IContent item, IMedia image, bool? openInViewer = null, object[]? link = null,
        bool? svg = null, string? cssClass = null)
    {
        var key = Guid.NewGuid();
        item.SetValue("diagrams", JsonSerializer.Serialize(new
        {
            layout = new Dictionary<string, object> { ["Umbraco.BlockList"] = new[] { new { contentKey = key } } },
            contentData = new[] { new { key, contentTypeKey = DrawioInstallation.ElementTypeKey, values = new object[]
            {
                new { alias = "image", culture = (string?)null, segment = (string?)null, editorAlias = "Umbraco.MediaPicker3", value = new[] { new { key = Guid.NewGuid(), mediaKey = image.Key } } },
                new { alias = "format", culture = (string?)null, segment = (string?)null, editorAlias = "Umbraco.TrueFalse", value = svg },
                new { alias = "cssClass", culture = (string?)null, segment = (string?)null, editorAlias = "Umbraco.TextBox", value = cssClass },
                new { alias = "openInViewer", culture = (string?)null, segment = (string?)null, editorAlias = "Umbraco.TrueFalse", value = openInViewer },
                new { alias = "link", culture = (string?)null, segment = (string?)null, editorAlias = "Umbraco.MultiUrlPicker", value = link }
            } } },
            settingsData = Array.Empty<object>(), expose = new[] { new { contentKey = key, culture = (string?)null, segment = (string?)null } }
        }));
    }

    private string ImagePath(IMedia image) => JsonDocument.Parse(image.GetValue<string>(Constants.Conventions.Media.File)!).RootElement.GetProperty("src").GetString()!["/media/".Length..];
    private void AssertMediaExists(IMedia image)
    {
        Assert.NotNull(Site.Services.GetRequiredService<IMediaService>().GetById(image.Key));
        var storage = Site.Services.GetRequiredService<MediaFileManager>().FileSystem;
        Assert.True(storage.FileExists(ImagePath(image)));
        if (image.ContentType.Key == DrawioInstallation.MediaTypeKey)
            foreach (var alias in new[] { "drawioSource", "drawioSvg" })
                Assert.True(storage.FileExists(image.GetValue<string>(alias)!["/media/".Length..]));
    }
    private void AssertMediaDeleted(IMedia image)
    {
        Assert.Null(Site.Services.GetRequiredService<IMediaService>().GetById(image.Key));
        var storage = Site.Services.GetRequiredService<MediaFileManager>().FileSystem;
        Assert.False(storage.FileExists(ImagePath(image)));
        if (image.ContentType.Key == DrawioInstallation.MediaTypeKey)
            foreach (var alias in new[] { "drawioSource", "drawioSvg" })
                Assert.False(storage.FileExists(image.GetValue<string>(alias)!["/media/".Length..]));
    }
}
