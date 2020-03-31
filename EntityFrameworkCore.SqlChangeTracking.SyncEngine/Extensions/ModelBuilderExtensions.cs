using System;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class ModelBuilderExtensions
    {
        public static ModelBuilder ConfigureSyncEngine(this ModelBuilder modelBuilder, Action<ChangeTrackingConfigurationBuilder>? configBuilderAction = null)
        {
            modelBuilder.ConfigureChangeTracking(configBuilderAction);

            modelBuilder.ApplyConfiguration(new LastSyncedChangeVersion());
            
            return modelBuilder;
        }
    }
}
