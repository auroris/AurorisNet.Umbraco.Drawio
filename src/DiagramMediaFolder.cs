using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace Drawio;

internal sealed class DiagramMediaFolder(IEntityService entities, ICoreScopeProvider scopes) :
    INotificationHandler<MediaMovingToRecycleBinNotification>,
    INotificationHandler<MediaDeletingNotification>,
    INotificationHandler<MediaEmptyingRecycleBinNotification>
{
    internal static readonly Guid Key = new("3ff1e030-bba1-4f59-bde9-e5f9131be6f1");

    public void Handle(MediaMovingToRecycleBinNotification notification)
        => Protect(notification.MoveInfoCollection.Select(move => move.Entity), notification, notification.Messages);

    public void Handle(MediaDeletingNotification notification)
        => Protect(notification.DeletedEntities, notification, notification.Messages);

    public void Handle(MediaEmptyingRecycleBinNotification notification)
        => Protect(notification.DeletedEntities ?? [], notification, notification.Messages);

    private void Protect(IEnumerable<IMedia> items, ICancelableNotification notification, EventMessages messages)
    {
        // A permanent delete raises its notification before taking the tree lock. Keep the checked path valid until commit.
        using var scope = scopes.CreateCoreScope(autoComplete: true);
        scope.WriteLock(Constants.Locks.MediaTree);
        if (entities.Get(Key, UmbracoObjectTypes.Media) is not IMediaEntitySlim { ContentTypeAlias: Constants.Conventions.MediaTypes.Folder } folder)
            return;

        // Umbraco reports the roots being removed. The folder's path includes its own ID and every ancestor.
        var path = folder.Path.Split(',').Select(int.Parse).ToHashSet();
        if (!items.Any(item => path.Contains(item.Id))) return;

        notification.Cancel = true;
        messages.Add(new EventMessage("Diagram uploads",
            $"The diagram upload folder '{folder.Name}' is protected. To delete a containing folder, move '{folder.Name}' out first.",
            EventMessageType.Warning) { IsDefaultEventMessage = true });
    }
}
