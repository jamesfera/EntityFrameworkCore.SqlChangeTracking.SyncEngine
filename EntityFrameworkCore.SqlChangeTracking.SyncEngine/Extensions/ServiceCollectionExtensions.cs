using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncEngine<TContext>(this IServiceCollection services, params Assembly[] assemblies) where TContext : DbContext
        {
            services.AddSingleton<ISyncEngine<TContext>, SyncEngine<TContext>>();

            services.AddSingleton<IChangeSetProcessorFactory<TContext>, ChangeSetProcessorFactory<TContext>>();

            services.AddSingleton<IChangeProcessor<TContext>, ChangeProcessor<TContext>>();

            services.AddSingleton(typeof(INotificationHandler<TableChangedNotification<TContext>>), typeof(TableChangedNotificationHandler<TContext>));

            services.AddMediatR(typeof(SqlChangeTrackingAnnotationNames));

            if (assemblies != null)
            {
                var processorInterfaceType = typeof(IChangeSetProcessor<,>);
                foreach (var assembly in assemblies)
                {
                    var processors = assembly.GetTypes().Where(t =>
                        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == processorInterfaceType)).ToArray();

                    foreach (var processor in processors)
                    {
                        services.AddTransient(processor.GetInterfaces().First(i => i.Name == processorInterfaceType.Name), processor);
                    }
                }
            }

            return services;
        }
    }
}
