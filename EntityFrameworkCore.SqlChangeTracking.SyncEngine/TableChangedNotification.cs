using System;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    internal class TableChangedNotification : ITableChangedNotification
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