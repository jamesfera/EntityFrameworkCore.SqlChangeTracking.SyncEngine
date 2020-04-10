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
        public static string GetNextChangeVersionExpression(IEntityType entityType, string syncContext)
        {
            var lastChangeVersionExpression = GetLastChangeVersionExpression(entityType, syncContext);

            var sql = ChangeTableSqlStatements.GetNextChangeVersionExpression(entityType, lastChangeVersionExpression);

            return sql;
        }

        public static string GetNextChangeSetExpression(IEntityType entityType, string syncContext)
        {
            var lastChangedVersionExpression = GetLastChangeVersionExpression(entityType, syncContext);

            var sql = ChangeTableSqlStatements.GetNextChangeSetExpression(entityType, lastChangedVersionExpression);

            return sql;
        }

        public static string GetAllChangeSetsExpression(IEntityType entityType, string syncContext)
        {
            var sql = $@"{ChangeTableSqlStatements.GetAllChangeSetsExpression(entityType, false)}
                        WHERE SYS_CHANGE_VERSION > ({GetLastChangeVersionExpression(entityType, syncContext)})
                        ORDER BY SYS_CHANGE_VERSION";

            return sql;
        }

        public static string GetLastChangeVersionExpression(IEntityType entityType, string syncContext)
        {
            var fullTableName = entityType.GetFullTableName();

            return $@"SELECT LastSyncedVersion FROM [dbo].{nameof(LastSyncedChangeVersion)}
			                WHERE TableName = '{fullTableName}' AND SyncContext = '{syncContext}'";
        }
    }
}
