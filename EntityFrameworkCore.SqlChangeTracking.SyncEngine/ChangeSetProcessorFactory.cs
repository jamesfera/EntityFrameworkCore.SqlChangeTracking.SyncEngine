using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;


namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class ChangeSetProcessorFactory<TContext> : IChangeSetProcessorFactory<TContext> where TContext : DbContext
    {
        private readonly IServiceProvider _serviceProvider;

        public ChangeSetProcessorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IEnumerable<object> GetChangeSetProcessorsForEntity(IEntityType entityType, string syncContext) 
        {
            try
            {
                var handlerType = typeof(IChangeSetProcessor<,>).MakeGenericType(entityType.ClrType, typeof(TContext));

                var ientity = entityType.ClrType.Assembly.GetType("AppleExpress.Inventory.Data.IEntity");

                //if (ientity != null)
                //    handlerType = typeof(IChangeSetProcessor<,>).MakeGenericType(ientity, typeof(TContext));

                return _serviceProvider.GetServices(handlerType);
            }
            catch (Exception ex)
            {
                //throw new MessageHandlerNotFoundException(messageType, ex);
                throw;
            }
        }
    }
}
