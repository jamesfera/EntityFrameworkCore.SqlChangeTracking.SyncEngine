using System;
using System.Collections.Generic;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class EntityTypeBuilderExtensions
    {
        public static EntityTypeBuilder<TEntity> WithSyncEngine<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder) where TEntity : class
            => (EntityTypeBuilder<TEntity>)WithSyncEngine((EntityTypeBuilder)entityTypeBuilder);

        public static EntityTypeBuilder WithSyncEngine(this EntityTypeBuilder entityTypeBuilder)
        {
            entityTypeBuilder.WithSqlChangeTracking();

            entityTypeBuilder.Metadata.SetSyncEngineEnabled(true);

            entityTypeBuilder.Metadata.Model.SafeAddEntityType(typeof(LastSyncedChangeVersion));

            return entityTypeBuilder;
        }
    }
}
