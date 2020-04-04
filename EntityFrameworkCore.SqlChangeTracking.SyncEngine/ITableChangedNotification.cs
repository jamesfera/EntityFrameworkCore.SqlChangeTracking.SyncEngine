using System;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface ITableChangedNotification
    {
        Type ContextType { get; }
        IEntityType EntityType { get; }
        string TableName { get; }
        ChangeOperation ChangeOperation { get; }
    }
}