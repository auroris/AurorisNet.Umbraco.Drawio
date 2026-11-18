using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common;
using Umbraco.Extensions;

namespace Drawio.Compatibility;

// Calls shared by the supported versions live here; a version adapter overrides only differing operations.
// Service resolution stays inside this boundary, including request-dependent published-content helpers.
internal abstract class UmbracoApi(IServiceProvider services) : IUmbracoApi
{
    protected IContentService Content { get; } = services.GetRequiredService<IContentService>();
    private readonly ICoreScopeProvider scopes = services.GetRequiredService<ICoreScopeProvider>();
    private readonly IMediaService media = services.GetRequiredService<IMediaService>();
    private readonly IRelationService relations = services.GetRequiredService<IRelationService>();
    private readonly MediaUrlGeneratorCollection urls = services.GetRequiredService<MediaUrlGeneratorCollection>();
    private readonly DataValueReferenceFactoryCollection references = services.GetRequiredService<DataValueReferenceFactoryCollection>();
    private readonly PropertyEditorCollection editors = services.GetRequiredService<PropertyEditorCollection>();

    public abstract int MajorVersion { get; }
    protected abstract IEnumerable<IContent> GetVersions(int contentId);

    public bool ContentVersionExists(int versionId) => services.GetRequiredService<IDocumentRepository>().GetVersion(versionId) is not null;

    public virtual T WithTreeWriteScope<T>(Func<T> work)
    {
        using var scope = scopes.CreateCoreScope();
        scope.WriteLock(Constants.Locks.ContentTree, Constants.Locks.MediaTree);
        var result = work();
        scope.Complete();
        return result;
    }

    public virtual void AfterCommit(IEnumerable<int> pages, IEnumerable<int> mediaIds, Action<HashSet<int>, HashSet<int>> work)
    {
        var pending = scopes.Context?.Enlist(nameof(DiagramMediaRetention), () => new PendingCleanup(), (completed, value) =>
        {
            if (completed && value is not null) work(value.Pages, value.Media);
        });
        pending?.Pages.UnionWith(pages);
        pending?.Media.UnionWith(mediaIds);
    }

    public virtual IEnumerable<int> GetContentBranch(int rootId)
    {
        yield return rootId;
        for (var page = 0; ; page++)
        {
            long total;
            foreach (var child in Content.GetPagedDescendants(rootId, page, 200, out total)) yield return child.Id;
            if ((page + 1L) * 200 >= total) yield break;
        }
    }

    public IEnumerable<Guid> GetMediaKeys(IContent content) =>
        references.GetAllReferences(content.Properties, editors).Select(reference => reference.Udi)
            .OfType<GuidUdi>().Where(udi => udi.EntityType == Constants.UdiEntityType.Media).Select(udi => udi.Guid);

    public IEnumerable<DiagramMedia> ResolveMedia(IEnumerable<Guid> keys)
    {
        var distinct = keys.Distinct().ToArray();
        return distinct.Length == 0 ? [] : media.GetByIds(distinct).Select(ToMedia).ToArray();
    }

    public IEnumerable<DiagramMedia> GetRetainedMedia(int contentId) => Content.GetById(contentId) is { } current
        ? ResolveMedia(GetVersions(contentId).Append(current).SelectMany(GetMediaKeys)) : [];

    public virtual bool RetentionRelationTypeExists => relations.GetRelationTypeByAlias(DiagramMediaRetention.RelationAlias) is not null;

    public virtual void EnsureRetentionRelationType()
    {
        if (!RetentionRelationTypeExists)
            relations.Save(new RelationType("Retained diagram media", DiagramMediaRetention.RelationAlias, false,
                Constants.ObjectTypes.Document, Constants.ObjectTypes.Media, true));
    }

    public virtual IReadOnlyList<DiagramRelation> GetRetentionRelations(int contentId)
        => (relations.GetByParentId(contentId, DiagramMediaRetention.RelationAlias) ?? [])
            .Select(relation => new DiagramRelation(relation.Id, relation.ChildId)).ToArray();

    public virtual void AddRetentionRelation(int contentId, int mediaId)
        => relations.Relate(contentId, mediaId, relations.GetRelationTypeByAlias(DiagramMediaRetention.RelationAlias)!);

    public virtual void DeleteRetentionRelation(int relationId)
    {
        if (relations.GetById(relationId) is { } relation) relations.Delete(relation);
    }

    public virtual bool HasMediaReferences(int mediaId) => relations.GetByChildId(mediaId).Any();
    public virtual DiagramMedia? GetMedia(int mediaId) => media.GetById(mediaId) is { } item ? ToMedia(item) : null;
    public virtual bool DeleteMedia(int mediaId) => media.GetById(mediaId) is { } item && media.Delete(item).Success;
    public virtual bool RecycleMedia(int mediaId)
        => media.GetById(mediaId) is { } item && (item.Trashed || media.MoveToRecycleBin(item).Success);

    public virtual DiagramPresentation ReadDiagram(IPublishedElement element)
    {
        var image = element.Value<MediaWithCrops>("image");
        var link = element.Value<Link>("link");
        var legacy = !element.HasProperty("openInViewer") && element.HasProperty("clickAction");
        var action = legacy
            ? (element.HasValue("clickAction") ? (DiagramClickAction)element.Value<int>("clickAction") : DiagramClickAction.None)
            : element.Value<bool>("openInViewer") ? DiagramClickAction.Viewer : DiagramClickAction.Link;
        return new(image?.Content.GetCropUrl(cacheBuster: true), image?.MediaUrl(),
            element.Value<string>("caption"), element.Value<string>("altText"), action,
            link?.Url, link?.Target, link?.Name,
            services.GetRequiredService<UmbracoHelper>().GetDictionaryValueOrDefault("Drawio.OpenDiagram", "Open diagram"));
    }

    private DiagramMedia ToMedia(IMedia item)
    {
        item.TryGetMediaPath(Constants.Conventions.Media.File, urls, out var path);
        return new(item.Id, item.Key, path, item.ContentType.Alias == Constants.Conventions.MediaTypes.Image);
    }

    private sealed class PendingCleanup
    {
        public HashSet<int> Pages { get; } = [];
        public HashSet<int> Media { get; } = [];
    }
}
