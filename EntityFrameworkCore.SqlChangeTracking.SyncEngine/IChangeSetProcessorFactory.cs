using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessorFactory<TDbContext> where TDbContext : DbContext
    {
        IEnumerable<object> GetChangeSetProcessorsForEntity(Type clrEntityType, string syncContext);
    }
}