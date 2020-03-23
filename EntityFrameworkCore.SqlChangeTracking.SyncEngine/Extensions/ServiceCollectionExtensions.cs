using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSqlChangeTrackingEngine<TContext>(this IServiceCollection services) where TContext : DbContext
        {
            services.AddSqlChangeTracking<TContext>();

            services.AddSingleton<ISqlChangeTrackingEngine, SqlChangeTrackingEngine<TContext>>();

            return services;
        }
    }
}
