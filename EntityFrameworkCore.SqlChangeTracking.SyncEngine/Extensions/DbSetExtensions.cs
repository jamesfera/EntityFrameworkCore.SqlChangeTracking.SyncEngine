using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions
{
    public static class DbSetExtensions
    {
        internal static IEnumerable<ChangeTrackingEntry<T>> GetChangesSinceVersion<T>(this DbSet<T> dbSet, long version, int maxResults) where T : class, new()
        {
            return dbSet.GetChangesSinceVersion(version).Take(maxResults);
        }
    }

    
}
