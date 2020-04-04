using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Extensions;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions
{
    public static class DbContextExtensions
    {
        public static async Task<long?> GetLastChangedVersionFor(this DbContext db, IEntityType entityType, string syncContext)
        {
            await using var innerContext = new ContextForQueryType<LastSyncedChangeVersion>(db.Database.GetDbConnection(), m => m.ApplyConfiguration(new LastSyncedChangeVersion()));

            var entry = await innerContext.Set<LastSyncedChangeVersion>().FirstOrDefaultAsync(t => t.SyncContext == syncContext && t.TableName == entityType.GetTableName());

            return entry?.LastSyncedVersion;
        }

        public static Task<long?> GetLastChangedVersionFor<T>(this DbContext db, string syncContext)
        {
            var entityType = db.Model.FindEntityType(typeof(T));

            return db.GetLastChangedVersionFor(entityType, syncContext);
        }

        public static IAsyncEnumerable<ChangeTrackingEntry<T>> GetChangesSinceLastVersion<T>(this DbContext db, IEntityType entityType, string syncContext) where T : class, new()
        {
            var lastVersion = db.GetLastChangedVersionFor(entityType, syncContext).Result ?? 0;

            return db.GetChangesSinceVersion<T>(entityType, lastVersion);
        }

        public static async Task SetLastChangedVersionFor(this DbContext db, IEntityType entityType, long version, string syncContext)
        {
            //await using var innerContext = new ContextForQueryType<LastSyncedChangeVersion>(db.Database.GetDbConnection(), m => m.ApplyConfiguration(new LastSyncedChangeVersion()));

            //innerContext.Set<LastSyncedChangeVersion>().Update(new LastSyncedChangeVersion()
            //{
            //    TableName = entityType.GetFullTableName(),
            //    SyncContext = syncContext,
            //    LastSyncedVersion = version
            //});

            ////await innerContext.Database.UseTransactionAsync(db.Database.CurrentTransaction.GetDbTransaction());

            //await innerContext.SaveChangesAsync(false);

            var tableName = nameof(LastSyncedChangeVersion);

            var keyColumn = nameof(LastSyncedChangeVersion.TableName);
            var versionColumn = nameof(LastSyncedChangeVersion.LastSyncedVersion);
            var key = entityType.GetTableName();

            var sqlString = $@"begin tran
                               UPDATE {tableName} WITH (serializable) set {versionColumn}={version}
                               WHERE {keyColumn}='{key}' AND SyncContext='{syncContext}'

                               if @@rowcount = 0
                               begin
                                  INSERT INTO {tableName} ({keyColumn}, SyncContext, {versionColumn}) values ('{key}', '{syncContext}' ,{version})
                               end
                            commit tran";

            await db.Database.ExecuteSqlRawAsync(sqlString);
        }

        //public static IList<T> SqlQuery<T>(this DbContext db, string sql, params object[] parameters) where T : class
        //{
        //    using (var innerContext = new ContextForQueryType<T>(db.Database.GetDbConnection()))
        //    {
        //        return innerContext.Set<T>().FromSqlRaw(sql, parameters).ToList();
        //    }
        //}

        private class ContextForQueryType<T> : DbContext where T : class
        {
            private readonly DbConnection connection;
            private readonly Action<ModelBuilder> _modelBuilderConfig;

            public ContextForQueryType(DbConnection connection, Action<ModelBuilder> modelBuilderConfig = null)
            {
                this.connection = connection;
                _modelBuilderConfig = modelBuilderConfig;
                
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer(connection);
                Database.AutoTransactionsEnabled = false;
                base.OnConfiguring(optionsBuilder);
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                _modelBuilderConfig?.Invoke(modelBuilder);

                if (_modelBuilderConfig == null)
                    modelBuilder.Entity<T>().HasNoKey();

                base.OnModelCreating(modelBuilder);
            }
        }
    }
}
