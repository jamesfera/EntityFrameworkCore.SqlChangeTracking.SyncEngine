using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class ChangeSetProcessorFactory<TContext> : IChangeSetProcessorFactory<TContext> where TContext : DbContext
    {
        readonly IServiceProvider _serviceProvider;
        readonly ILogger<ChangeSetProcessorFactory<TContext>> _logger;

        public ChangeSetProcessorFactory(IServiceProvider serviceProvider, ILogger<ChangeSetProcessorFactory<TContext>> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public IEnumerable<object> GetChangeSetProcessorsForEntity(Type clrEntityType, string syncContext) 
        {
            try
            {
                List<object> services = new List<object>();

                var handlerType = typeof(IChangeSetProcessor<,>).MakeGenericType(clrEntityType, typeof(TContext));

                return _serviceProvider.GetServices(handlerType);

                //services.AddRange();

                //foreach (var @interface in entityType.ClrType.GetInterfaces())
                //{
                //    handlerType = typeof(IChangeSetProcessor<,>).MakeGenericType(@interface, typeof(TContext));
                //    services.AddRange(_serviceProvider.GetServices(handlerType));
                //}

                //return services;
            }
            catch (Exception ex)
            {
                //throw new MessageHandlerNotFoundException(messageType, ex);
                throw;
            }
        }
    }
}
