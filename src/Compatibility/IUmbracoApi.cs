using Umbraco.Cms.Core.Models.PublishedContent;

namespace Drawio.Compatibility;

// Only the operations this package uses. Umbraco entities are normalized at this boundary.
internal interface IUmbracoApi
{
    int MajorVersion { get; }
    T WithTreeWriteScope<T>(Func<T> work);
    void AfterCommit(IEnumerable<int> pages, IEnumerable<int> media, Action<HashSet<int>, HashSet<int>> work);
    IEnumerable<int> GetContentBranch(int rootId);
    IEnumerable<DiagramMedia> GetReferencedMedia(int contentId);
    bool RetentionRelationTypeExists { get; }
    void EnsureRetentionRelationType();
    IReadOnlyList<DiagramRelation> GetRetentionRelations(int contentId);
    void AddRetentionRelation(int contentId, int mediaId);
    void DeleteRetentionRelation(int relationId);
    bool HasMediaReferences(int mediaId);
    DiagramMedia? GetMedia(int mediaId);
    bool DeleteMedia(int mediaId);
    bool RecycleMedia(int mediaId);
    long? GetMediaFileSize(string path);
    Stream OpenMediaFile(string path);
    DiagramPresentation ReadDiagram(IPublishedElement element);
}

internal sealed record DiagramRelation(int Id, int MediaId);
internal sealed record DiagramMedia(int Id, Guid Key, string? Path, bool IsImage);
internal sealed record DiagramPresentation(string? ImageUrl, string? Caption, string? AltText,
    DiagramClickAction Action, string? LinkUrl, string? LinkTarget, string? LinkName, string Label);
