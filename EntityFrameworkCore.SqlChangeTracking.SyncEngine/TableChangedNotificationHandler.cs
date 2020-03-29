using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class TableChangedNotificationHandler<TContext> : INotificationHandler<TableChangedNotification<TContext>> where TContext : DbContext
    {
        IChangeProcessor<TContext> _changeProcessor;

        public TableChangedNotificationHandler(IChangeProcessor<TContext> changeProcessor)
        {
            _changeProcessor = changeProcessor;
        }

        public async Task Handle(TableChangedNotification<TContext> notification, CancellationToken cancellationToken)
        {
            await _changeProcessor.ProcessChangesForTable(notification.EntityType);
        }
    }

    public class TableChangedNotification<TContext> : INotification where TContext : DbContext
    {
        public TableChangedNotification(IEntityType entityType, ChangeOperation changeOperation)
        {
            EntityType = entityType;
            ChangeOperation = changeOperation;
        }

        public IEntityType EntityType { get; }
        public string TableName => EntityType.GetTableName();
        public ChangeOperation ChangeOperation { get; }
    }
}
