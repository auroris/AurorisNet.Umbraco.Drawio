using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Drawio.Compatibility;

// Only the operations this package uses. Umbraco entities are normalized at this boundary.
internal interface IUmbracoApi
{
    int MajorVersion { get; }
    T WithTreeWriteScope<T>(Func<T> work);
    void AfterCommit(IEnumerable<int> pages, IEnumerable<int> media, Action<HashSet<int>, HashSet<int>> work);
    IEnumerable<int> GetContentBranch(int rootId);
    bool ContentVersionExists(int versionId);
    IEnumerable<Guid> GetMediaKeys(IContent content);
    IEnumerable<DiagramMedia> ResolveMedia(IEnumerable<Guid> keys);
    IEnumerable<DiagramMedia> GetRetainedMedia(int contentId);
    bool RetentionRelationTypeExists { get; }
    void EnsureRetentionRelationType();
    IReadOnlyList<DiagramRelation> GetRetentionRelations(int contentId);
    void AddRetentionRelation(int contentId, int mediaId);
    void DeleteRetentionRelation(int relationId);
    bool HasMediaReferences(int mediaId);
    DiagramMedia? GetMedia(int mediaId);
    bool DeleteMedia(int mediaId);
    bool RecycleMedia(int mediaId);
    DiagramPresentation ReadDiagram(IPublishedElement element);
}

internal sealed record DiagramRelation(int Id, int MediaId);
internal sealed record DiagramMedia(int Id, Guid Key, string? Path, bool IsDiagram);
internal sealed record DiagramPresentation(string? ImageUrl, bool InlineSvg, string? OriginalUrl, string? Map, string? Caption, string? AltText, string? CssClass,
    DiagramClickAction Action, string? LinkUrl, string? LinkTarget, string? LinkName, string Label);
