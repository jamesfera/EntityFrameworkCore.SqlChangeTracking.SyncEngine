using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface ITableChangedNotificationDispatcher
    {
        Task Dispatch(ITableChangedNotification notification, CancellationToken cancellationToken);
    }

    internal class TableChangedNotificationDispatcher : ITableChangedNotificationDispatcher
    {
        IServiceProvider _serviceProvider;
        ILogger<TableChangedNotificationDispatcher> _logger;

        public TableChangedNotificationDispatcher(IServiceProvider serviceProvider, ILogger<TableChangedNotificationDispatcher> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task Dispatch(ITableChangedNotification notification, CancellationToken cancellationToken)
        {
            var handlers = _serviceProvider.GetServices<ITableChangedNotificationHandler>().ToList();

            var handlerTasks = handlers.Select(async h =>
            {
                try
                {
                    await h.Handle(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling notification: {tableChangedNotification} Handler: {notificationHandler}", notification, h.GetType().PrettyName());
                }
            });

            await Task.WhenAll(handlerTasks);
        }
    }

    public interface ITableChangedNotificationHandler
    {
        Task Handle(ITableChangedNotification notification, CancellationToken cancellationToken);
    }

    public interface ITableChangedNotification
    {
        Type ContextType { get; }
        IEntityType EntityType { get; }
        string TableName { get; }
        ChangeOperation ChangeOperation { get; }
    }

    public class TableChangedNotification : ITableChangedNotification
    {
        public TableChangedNotification(Type contextType, IEntityType entityType, ChangeOperation changeOperation)
        {
            ContextType = contextType;
            EntityType = entityType;
            ChangeOperation = changeOperation;
        }

        public Type ContextType { get; }
        public IEntityType EntityType { get; }
        public string TableName => EntityType.GetTableName();
        public ChangeOperation ChangeOperation { get; }
    }
}
