using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;


namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class ChangeSetProcessorFactory<TDbContext> : IChangeSetProcessorFactory<TDbContext> where TDbContext : DbContext
    {
        private readonly IServiceProvider _serviceProvider;

        public ChangeSetProcessorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public object GetChangeSetProcessorForEntity(IEntityType entityType) 
        {
            try
            {
                var handlerType = typeof(IChangeSetProcessor<,>).MakeGenericType(entityType.ClrType, typeof(TDbContext));

                return _serviceProvider.GetRequiredService(handlerType);
            }
            catch (Exception ex)
            {
                //throw new MessageHandlerNotFoundException(messageType, ex);
                throw;
            }
        }
    }
}
