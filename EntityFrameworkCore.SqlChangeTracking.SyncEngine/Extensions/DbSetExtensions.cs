using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class DbSetExtensions
    {
        private static void Validate<T>(DbSet<T> dbSet) where T : class
        {
            var context = dbSet.GetService<ICurrentDbContext>().Context;

            var entityType = context.Model.FindEntityType(typeof(T));
            
            var changeTrackingEnabled = entityType.IsSqlChangeTrackingEnabled();

            if(!changeTrackingEnabled)
                throw new ArgumentException($"Change tracking is not enabled for Entity: '{entityType.Name}'. Call '.WithSqlChangeTracking()' at Entity build time.");
        }

        internal static string[] GetColumnNames(this IEntityType entityType)
        {
            return entityType.GetProperties().Select(p => p.GetColumnName()).ToArray();
        }
    }
}
