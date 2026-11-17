using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Configuration;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace Drawio.Compatibility;

internal static class UmbracoCompatibility
{
    internal static Type ImplementationType(int major) => major switch
    {
        17 => typeof(Umbraco17Api),
        18 => typeof(Umbraco18Api),
        _ => throw new NotSupportedException($"draw.io supports Umbraco 17 and 18; Umbraco {major} needs its own tested adapter.")
    };

    internal static void Register(IUmbracoBuilder builder)
    {
        builder.Services.AddTransient<IUmbracoApi>(services => (IUmbracoApi)ActivatorUtilities.CreateInstance(services,
            ImplementationType(services.GetRequiredService<IUmbracoVersion>().Version.Major)));
        builder.AddNotificationHandler<ContentSavedNotification, UmbracoNotifications>();
        builder.AddNotificationHandler<ContentPublishedNotification, UmbracoNotifications>();
        builder.AddNotificationHandler<ContentDeletedVersionsNotification, UmbracoNotifications>();
        builder.AddNotificationHandler<ContentDeletingNotification, UmbracoNotifications>();
    }
}

// Thin CMS entry points normalize notifications before calling the retention policy.
internal sealed class UmbracoNotifications(DiagramMediaRetention retention) :
    INotificationHandler<ContentSavedNotification>, INotificationHandler<ContentPublishedNotification>,
    INotificationHandler<ContentDeletedVersionsNotification>, INotificationHandler<ContentDeletingNotification>
{
    public void Handle(ContentSavedNotification notification) => retention.Track(notification.SavedEntities.Select(item => item.Id));
    public void Handle(ContentPublishedNotification notification) => retention.Track(notification.PublishedEntities.Select(item => item.Id));
    // These notifications run after the original scope has committed and released its context.
    public void Handle(ContentDeletedVersionsNotification notification) => retention.Clean([notification.Id], []);
    public void Handle(IEnumerable<ContentDeletedVersionsNotification> notifications)
        => retention.Clean(notifications.Select(notification => notification.Id).ToHashSet(), []);
    public void Handle(ContentDeletingNotification notification) => retention.Deleting(notification.DeletedEntities.Select(item => item.Id));
}
