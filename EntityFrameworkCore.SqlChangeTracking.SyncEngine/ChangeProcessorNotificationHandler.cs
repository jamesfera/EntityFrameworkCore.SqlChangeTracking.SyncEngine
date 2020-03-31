using System;
using System.Collections.Generic;
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

            await (Task)processorType.GetMethod(nameof(IChangeProcessor<DbContext>.ProcessChangesForTable)).Invoke(changeProcessor, new[] {notification.EntityType});
        }
    }
}
