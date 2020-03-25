using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions
{
    public static class DbContextExtensions
    {
        public static Task<long?> GetLastChangedVersionFor(this DbContext db, IEntityType entityType)
        {
            using var innerContext = new ContextForQueryType<LastSyncedChangeVersion>(db.Database.GetDbConnection());
            
            return Task.FromResult(innerContext.Set<LastSyncedChangeVersion>().FirstOrDefaultAsync(t => t.TableName == entityType.GetTableName())?.Result?
                .LastSyncedVersion);
        }

        public static async Task SetLastChangedVersionFor(this DbContext db, IEntityType entityType, long version)
        {
            var tableName = nameof(LastSyncedChangeVersion);

            var keyColumn = nameof(LastSyncedChangeVersion.TableName);
            var versionColumn = nameof(LastSyncedChangeVersion.LastSyncedVersion);
            var key = entityType.GetTableName();

            var sqlString = $@"begin tran
                               UPDATE {tableName} WITH (serializable) set {versionColumn}={version}
                               WHERE {keyColumn}='{key}'

                               if @@rowcount = 0
                               begin
                                  INSERT INTO {tableName} ({keyColumn}, {versionColumn}) values ('{key}',{version})
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

            public ContextForQueryType(DbConnection connection)
            {
                this.connection = connection;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer(connection, options => options.EnableRetryOnFailure());

                base.OnConfiguring(optionsBuilder);
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<T>().HasNoKey();
                base.OnModelCreating(modelBuilder);
            }
        }
    }
}
