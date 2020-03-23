using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;


namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeHandlerFactory<TDbContext> where TDbContext : DbContext
    {
        IChangeHandler<object,TDbContext> GetChangeHandlerForEntity(IEntityType entityType);
    }
}
