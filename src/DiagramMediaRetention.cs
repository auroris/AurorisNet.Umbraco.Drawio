using Drawio.Compatibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;

namespace Drawio;

/// <summary>Keeps page-to-diagram relations until the last retained version no longer needs the media.</summary>
internal sealed class DiagramMediaRetention(IUmbracoApi cms, IOptionsMonitor<DrawioOptions> options, ILogger<DiagramMediaRetention> logger)
{
    internal const string RelationAlias = "aurorisDrawioRetainedMedia";

    internal void Track(IEnumerable<IContent> pages)
    {
        foreach (var page in pages)
        {
            var candidates = cms.ResolveMedia(cms.GetMediaKeys(page)).Where(IsDiagramImage).Select(image => image.Id).ToArray();
            if (candidates.Length == 0 || !cms.RetentionRelationTypeExists) continue;
            cms.WithTreeWriteScope(() =>
            {
                // The notification can arrive after a concurrent deletion or version prune.
                if (!cms.ContentVersionExists(page.VersionId)) return false;
                var existing = cms.GetRetentionRelations(page.Id).Select(relation => relation.MediaId);
                foreach (var id in candidates.Except(existing))
                    if (cms.GetMedia(id) is { } image && IsDiagramImage(image)) cms.AddRetentionRelation(page.Id, id);
                return true;
            });
        }
    }

    internal void Deleting(IEnumerable<int> roots)
    {
        // Capture the whole branch before Umbraco removes its nodes and relations.
        var pages = roots.SelectMany(cms.GetContentBranch).ToHashSet();
        var candidates = pages.SelectMany(cms.GetRetentionRelations).Select(relation => relation.MediaId).ToHashSet();
        cms.AfterCommit(pages, candidates, Clean);
    }

    internal void Clean(IEnumerable<int> pages, HashSet<int> candidates)
    {
        try
        {
            cms.WithTreeWriteScope(() =>
            {
                foreach (var id in pages) candidates.UnionWith(Prune(id));
                foreach (var id in candidates) RemoveUnreferenced(id);
                return true;
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean up retained diagram media after content deletion");
        }
    }

    private HashSet<int> Prune(int contentId)
    {
        if (!cms.RetentionRelationTypeExists) return []; // The installation migration has not run yet.
        var existing = cms.GetRetentionRelations(contentId);
        var retained = cms.GetRetainedMedia(contentId).Where(IsDiagramImage).Select(image => image.Id).ToHashSet();
        var previous = existing.Select(relation => relation.MediaId).ToHashSet();
        foreach (var id in retained.Except(previous)) cms.AddRetentionRelation(contentId, id);
        foreach (var relation in existing.Where(relation => !retained.Contains(relation.MediaId))) cms.DeleteRetentionRelation(relation.Id);
        return previous.Except(retained).ToHashSet();
    }

    private static bool IsDiagramImage(DiagramMedia image) => image.IsImage && IsDiagramFile(image.Path, image.Key);

    internal static bool IsDiagramFile(string? path, Guid key) => path is not null
        && path.EndsWith($"/diagram-{key:N}.png", StringComparison.OrdinalIgnoreCase);

    private void RemoveUnreferenced(int mediaId)
    {
        var handling = options.CurrentValue.SupersededImages;
        if (handling == SupersededImageHandling.Keep || cms.GetMedia(mediaId) is not { } image || !IsDiagramImage(image)
            || cms.HasMediaReferences(mediaId)) return;
        var removed = handling == SupersededImageHandling.Delete ? cms.DeleteMedia(mediaId) : cms.RecycleMedia(mediaId);
        if (removed) logger.LogInformation("Removed unreferenced diagram {MediaKey} ({Handling})", image.Key, handling);
        else logger.LogWarning("Umbraco cancelled removing unreferenced diagram {MediaKey}", image.Key);
    }
}
