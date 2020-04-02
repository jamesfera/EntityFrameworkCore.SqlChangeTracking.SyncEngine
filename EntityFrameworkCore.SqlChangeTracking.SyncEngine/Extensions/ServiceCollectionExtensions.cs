using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncEngine<TContext>(this IServiceCollection services, params Assembly[] assemblies) where TContext : DbContext
        {
            services.AddSingleton<ISyncEngine<TContext>, SyncEngine<TContext>>();

            services.AddScoped<IChangeSetProcessorFactory<TContext>, ChangeSetProcessorFactory<TContext>>();

            services.AddSingleton<IChangeProcessor<TContext>, ChangeProcessor<TContext>>();

            services.AddSingleton<ITableChangedNotificationDispatcher, TableChangedNotificationDispatcher>();

            services.AddTransient<ITableChangedNotificationHandler, ChangeProcessorNotificationHandler>();

            //services.AddScoped<DbSetExtensions.ICurrentTrackingContext, DbSetExtensions.CurrentTrackingContext>();
            //services.AddScoped<DbSetExtensions.ICurrentContextSetter, DbSetExtensions.CurrentTrackingContext>();

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

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, Action<SyncEngineOptions>? optionsBuilder, params Assembly[] assemblies) where TContext : DbContext
        {
            var options = new SyncEngineOptions();

            optionsBuilder?.Invoke(options);

            services.AddHostedService(s => new SyncEngineHostedService<TContext>(s.GetRequiredService<ISyncEngine<TContext>>(), options));
            services.AddSyncEngine<TContext>(assemblies);

            return services;
        }

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, params Assembly[] assemblies) where TContext : DbContext
        {
            return services.AddHostedSyncEngineService<TContext>(null, assemblies);
        }
    }
}
