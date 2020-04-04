using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    internal class ChangeProcessorNotificationHandler : ITableChangedNotificationHandler
    {
        IServiceProvider _serviceProvider;
        
        public ChangeProcessorNotificationHandler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task Handle(ITableChangedNotification notification, CancellationToken cancellationToken)
        {
            var processorType = typeof(IChangeProcessor<>).MakeGenericType(notification.ContextType);

            var changeProcessor = _serviceProvider.GetService(processorType);

            var processChangesMethod = processorType.GetMethod(nameof(IChangeProcessor<DbContext>.ProcessChangesFor)).MakeGenericMethod(notification.EntityType.ClrType);

            var getChangesFunc = this.getChangesFunc(notification.EntityType, notification.ContextType);

            await (Task)processChangesMethod.Invoke(changeProcessor, new[] {getChangesFunc});
        }

        Delegate getChangesFunc(IEntityType entityType, Type dbContextType)
        {
            var getChangesMethodInfo = typeof(Extensions.DbSetExtensions).GetMethod(nameof(Extensions.DbSetExtensions
                    .GetChangesSinceVersion), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);

            var dbSetType = typeof(DbSet<>).MakeGenericType(entityType.ClrType);

            var dbSetPropertyInfo = dbContextType.GetProperties().FirstOrDefault(p => p.PropertyType == dbSetType);

            if (dbSetPropertyInfo == null)
                throw new Exception($"No Property of type {dbSetType.PrettyName()} found on {dbContextType.Name}");

            var dbContextParameterExpression = Expression.Parameter(dbContextType, "dbContext");
            var dbSetPropertyExpression = Expression.Property(dbContextParameterExpression, dbSetPropertyInfo);

            var versionParameter = Expression.Parameter(typeof(long), "version");
            var maxResultsParameter = Expression.Parameter(typeof(int), "maxResults");

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbSetPropertyExpression, versionParameter, maxResultsParameter);

            var lambda = Expression.Lambda(getChangesCallExpression, dbContextParameterExpression, versionParameter, maxResultsParameter);

            return lambda.Compile();
        }
    }
}
