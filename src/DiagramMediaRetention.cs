using Drawio.Compatibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drawio;

/// <summary>Keeps page-to-diagram relations until the last retained version no longer needs the media.</summary>
internal sealed class DiagramMediaRetention(IUmbracoApi cms, IOptionsMonitor<DrawioOptions> options, ILogger<DiagramMediaRetention> logger)
{
    internal const string RelationAlias = "aurorisDrawioRetainedMedia";

    internal void Track(IEnumerable<int> pages)
    {
        foreach (var id in pages) Synchronize(id, prune: false);
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
                foreach (var id in pages) candidates.UnionWith(Synchronize(id, prune: true));
                foreach (var id in candidates) RemoveUnreferenced(id);
                return true;
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean up retained diagram media after content deletion");
        }
    }

    internal HashSet<int> Synchronize(int contentId, bool prune)
    {
        if (!cms.RetentionRelationTypeExists) return []; // The installation migration has not run yet.
        return cms.WithTreeWriteScope(() =>
        {
            var existing = cms.GetRetentionRelations(contentId);
            var retained = cms.GetReferencedMedia(contentId).Where(IsDiagramImage).Select(image => image.Id).ToHashSet();
            var previous = existing.Select(relation => relation.MediaId).ToHashSet();
            foreach (var id in retained.Except(previous)) cms.AddRetentionRelation(contentId, id);
            if (prune)
                foreach (var relation in existing.Where(relation => !retained.Contains(relation.MediaId))) cms.DeleteRetentionRelation(relation.Id);
            return prune ? previous.Except(retained).ToHashSet() : [];
        });
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
