using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Monitoring;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncEngine<TContext>(this IServiceCollection services, string syncContext, params Assembly[] assemblies) where TContext : DbContext
        {
            services.AddTransient<ISyncEngine<TContext>, SyncEngine<TContext>>();

            //services.AddScoped<IChangeSetProcessorFactory<TContext>, ChangeSetProcessorFactory<TContext>>();

            //services.AddTransient<IChangeProcessor<TContext>, ChangeProcessor<TContext>>();

            services.TryAddScoped<IChangeProcessorFactory<TContext>, ChangeProcessorFactory<TContext>>();

            //var processorFactoryRegistry = new ChangeSetProcessorFactoryRegistry<TContext>();

            //services.AddSingleton<IChangeSetProcessorFactoryRegistry<TContext>>(processorFactoryRegistry);
            

            //services.AddSingleton<ITableChangedNotificationDispatcher, TableChangedNotificationDispatcher>();

            services.TryAddSingleton<IDatabaseChangeMonitor, DatabaseChangeMonitor>();

            //services.AddTransient<ITableChangedNotificationHandler, ChangeProcessorNotificationHandler>();

            //services.AddScoped<DbSetExtensions.ICurrentTrackingContext, DbSetExtensions.CurrentTrackingContext>();
            //services.AddScoped<DbSetExtensions.ICurrentContextSetter, DbSetExtensions.CurrentTrackingContext>();

            if (assemblies != null)
            {
                var processorInterfaceType = typeof(IChangeSetProcessor<,>);
                foreach (var assembly in assemblies)
                {
                    var processors = assembly.GetTypes().Where(t =>
                        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == processorInterfaceType && i.GenericTypeArguments[1] == typeof(TContext))).ToArray();

                    foreach (var processorType in processors)
                    {
                        var serviceType = processorType.GetInterfaces().First(i => i.Name == processorInterfaceType.Name);

                        services.AddScoped(serviceType, processorType);
                        services.AddSingleton<IChangeSetProcessorRegistration>(new ChangeSetProcessorRegistration(new KeyValuePair<string, Type>(syncContext, serviceType)));
                    }
                }
            }

            return services;
        }

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, Action<SyncEngineOptions>? optionsBuilder, params Assembly[] assemblies) where TContext : DbContext
        {
            var options = new SyncEngineOptions();

            optionsBuilder?.Invoke(options);

            services.AddHostedService(s => new SyncEngineHostedService<TContext>(s.GetRequiredService<ISyncEngine<TContext>>(), options));
            services.AddSyncEngine<TContext>(options.SyncContext, assemblies);

            return services;
        }

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, params Assembly[] assemblies) where TContext : DbContext
        {
            return services.AddHostedSyncEngineService<TContext>(null, assemblies);
        }
    }
}
