using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class EntityTypeBuilderExtensions
    {
        public static EntityTypeBuilder<TEntity> WithSyncEngine<TEntity>(this EntityTypeBuilder<TEntity> entity, DbSet<TEntity> dbSet) where TEntity : class
        {
            
            //dbSet.GetChangesSinceVersion()

            return entity;
        }
    }
}
