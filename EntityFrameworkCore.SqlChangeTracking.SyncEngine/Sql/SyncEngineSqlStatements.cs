using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EntityFrameworkCore.SqlChangeTracking.Sql;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Sql
{
    public static class SyncEngineSqlStatements
    {
        public static string GetNextChangeVersion(IEntityType entityType, string syncContext)
        {
            var lastChangeVersionExpression = GetLastChangeVersion(entityType, syncContext);

            var sql = ChangeTableSqlStatements.GetNextChangeVersion(entityType, lastChangeVersionExpression);

            return sql;
        }

        public static string GetNextChangeSet(IEntityType entityType, string syncContext)
        {
            var getNextChangeVersionExpression = GetNextChangeVersion(entityType, syncContext);

            //var lastChangedVersionExpression = GetLastChangedVersion(entityType, syncContext);

            var sql = ChangeTableSqlStatements.GetNextChangeSet(entityType, "0", getNextChangeVersionExpression);

            return sql;
        }

        public static string GetLastChangeVersion(IEntityType entityType, string syncContext)
        {
            var fullTableName = entityType.GetFullTableName();

            return $@"SELECT LastSyncedVersion FROM [Inventory].[dbo].{nameof(LastSyncedChangeVersion)}
			                WHERE TableName = '{fullTableName}' AND SyncContext = '{syncContext}'";
        }
    }
}
