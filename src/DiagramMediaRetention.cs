using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Drawio;

/// <summary>Keeps page-to-diagram relations until the last retained version no longer needs the media.</summary>
internal sealed class DiagramMediaRetention(
    ICoreScopeProvider scopes,
    IContentService content,
    IMediaService media,
    IRelationService relations,
    DataValueReferenceFactoryCollection references,
    PropertyEditorCollection editors,
    MediaUrlGeneratorCollection urlGenerators,
    IOptionsMonitor<DrawioOptions> options,
    ILogger<DiagramMediaRetention> logger) :
    INotificationHandler<ContentSavedNotification>,
    INotificationHandler<ContentPublishedNotification>,
    INotificationHandler<ContentDeletedVersionsNotification>,
    INotificationHandler<ContentDeletingNotification>
{
    internal const string RelationAlias = "aurorisDrawioRetainedMedia";

    public void Handle(ContentSavedNotification notification)
    {
        foreach (var item in notification.SavedEntities) Synchronize(item.Id, prune: false);
    }

    public void Handle(ContentPublishedNotification notification)
    {
        foreach (var item in notification.PublishedEntities) Synchronize(item.Id, prune: false);
    }

    // Non-cancelable notifications are dispatched after Umbraco commits and releases the original scope context.
    public void Handle(ContentDeletedVersionsNotification notification) => Clean([notification.Id], []);

    public void Handle(IEnumerable<ContentDeletedVersionsNotification> notifications)
        => Clean(notifications.Select(n => n.Id).ToHashSet(), []);

    public void Handle(ContentDeletingNotification notification)
    {
        // A branch deletion can notify only its root. Capture descendants before the tree and its relations disappear.
        var ids = new HashSet<int>();
        foreach (var item in notification.DeletedEntities)
        {
            ids.Add(item.Id);
            long total;
            for (var page = 0; ; page++)
            {
                foreach (var child in content.GetPagedDescendants(item.Id, page, 200, out total)) ids.Add(child.Id);
                if ((page + 1L) * 200 >= total) break;
            }
        }
        var candidates = ids.SelectMany(id => relations.GetByParentId(id, RelationAlias) ?? []).Select(r => r.ChildId).ToHashSet();
        AfterCommit(ids, candidates);
    }

    private void AfterCommit(IEnumerable<int> pages, IEnumerable<int> mediaIds)
    {
        var pending = scopes.Context?.Enlist(nameof(DiagramMediaRetention), () => new PendingCleanup(), (completed, work) =>
        {
            if (!completed || work is null) return;
            Clean(work.Pages, work.Media);
        });
        pending?.Pages.UnionWith(pages);
        pending?.Media.UnionWith(mediaIds);
    }

    private void Clean(IEnumerable<int> pages, HashSet<int> candidates)
    {
        try
        {
            using var scope = scopes.CreateCoreScope();
            scope.WriteLock(Constants.Locks.ContentTree, Constants.Locks.MediaTree);
            foreach (var id in pages) candidates.UnionWith(Synchronize(id, prune: true));
            foreach (var id in candidates) RemoveUnreferenced(id);
            scope.Complete();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean up retained diagram media after content deletion");
        }
    }

    /// <summary>Rebuilds a page's retention from Umbraco's version data; never creates a parallel version ledger.</summary>
    internal HashSet<int> Synchronize(int contentId, bool prune)
    {
        var type = relations.GetRelationTypeByAlias(RelationAlias);
        if (type is null) return []; // The installation migration has not run yet.
        using var scope = scopes.CreateCoreScope();
        scope.WriteLock(Constants.Locks.ContentTree, Constants.Locks.MediaTree);
        var existing = (relations.GetByParentId(contentId, RelationAlias) ?? []).ToArray();
        var keys = new HashSet<Guid>();
        if (content.GetById(contentId) is { } current)
        {
            // Include the stored current draft and published values, and all history, including protected versions.
            foreach (var item in content.GetVersions(contentId).Append(current))
                foreach (var reference in references.GetAllReferences(item.Properties, editors))
                    if (reference.Udi is GuidUdi udi && udi.EntityType == Constants.UdiEntityType.Media) keys.Add(udi.Guid);
        }
        var retained = media.GetByIds(keys).Where(IsDiagramImage).Select(image => image.Id).ToHashSet();
        var previous = existing.Select(r => r.ChildId).ToHashSet();
        foreach (var id in retained.Except(previous)) relations.Relate(contentId, id, type);
        if (prune)
            foreach (var relation in existing.Where(r => !retained.Contains(r.ChildId))) relations.Delete(relation);
        scope.Complete();
        return prune ? previous.Except(retained).ToHashSet() : [];
    }

    private bool IsDiagramImage(IMedia item) => item.ContentType.Alias == Constants.Conventions.MediaTypes.Image
        && item.TryGetMediaPath(Constants.Conventions.Media.File, urlGenerators, out var path) && IsDiagramFile(path, item.Key);

    internal static bool IsDiagramFile(string? path, Guid key) => path is not null
        && path.EndsWith($"/diagram-{key:N}.png", StringComparison.OrdinalIgnoreCase);

    private void RemoveUnreferenced(int mediaId)
    {
        var handling = options.CurrentValue.SupersededImages;
        if (handling == SupersededImageHandling.Keep || media.GetById(mediaId) is not { } image || !IsDiagramImage(image)
            || relations.GetByChildId(mediaId).Any()) return;
        var removed = handling == SupersededImageHandling.Delete ? media.Delete(image).Success
            : image.Trashed || media.MoveToRecycleBin(image).Success;
        if (removed) logger.LogInformation("Removed unreferenced diagram {MediaKey} ({Handling})", image.Key, handling);
        else logger.LogWarning("Umbraco cancelled removing unreferenced diagram {MediaKey}", image.Key);
    }

    private sealed class PendingCleanup
    {
        public HashSet<int> Pages { get; } = [];
        public HashSet<int> Media { get; } = [];
    }
}
