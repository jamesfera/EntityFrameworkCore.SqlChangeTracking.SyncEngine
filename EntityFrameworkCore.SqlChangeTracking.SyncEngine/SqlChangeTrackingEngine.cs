using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppleExpress.Inventory.Legacy.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeHandler<in TEntity, TDbContext> where TDbContext : DbContext
    {
        void ProcessChanges(IEnumerable<TEntity> changes, ChangeProcessorContext<TDbContext> context);
    }


    public abstract class ChangeHandler<TEntity, TDbContext> : IChangeHandler<TEntity, TDbContext> where TDbContext : DbContext
    {
        public void ProcessChanges(IEnumerable<TEntity> changes, ChangeProcessorContext<TDbContext> context)
        {

        }


    }

    public class ChangeProcessorContext<TDbContext>
    {
        public TDbContext DbContext { get; private set; }

        public void SkipRecordCurrentVersion()
        {

        }
    }
    public interface ISqlChangeTrackingEngine : IDisposable
    {
        Task Start();
        void Stop();
    }
    public class SqlChangeTrackingEngine<TDbContext> : ISqlChangeTrackingEngine where TDbContext : DbContext
    {
        private IConfiguration _configuration;

        private TDbContext _dbContext;
        private ILogger<SqlChangeTrackingEngine<TDbContext>> _logger;

        private List<SqlDependencyEx> _sqlTableDependencies = new List<SqlDependencyEx>();

        public SqlChangeTrackingEngine(IConfiguration configuration, TDbContext dbContext, ILogger<SqlChangeTrackingEngine<TDbContext>> logger)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
        }

        

        public Task Start()
        {
            var trackingEntityTypes = _dbContext.Model.GetEntityTypes().Where(e => e.IsSqlChangeTrackingEnabled()).ToArray();

            _logger.LogInformation("Found {entityTrackingCount} Entities with Sql Change Tracking enabled", trackingEntityTypes.Length);

            foreach (var entityType in trackingEntityTypes)
            {
                var tableName = entityType.GetTableName();

                var sqlTableDependency = new SqlDependencyEx(_configuration.GetConnectionString("LegacyConnection"), "Inventory", tableName);

                sqlTableDependency.TableChanged += (object sender, SqlDependencyEx.TableChangedEventArgs e) =>
                {
                    var handlerContext = new ChangeProcessorContext<TDbContext>();

                    //ch.ProcessChanges(changes, handlerContext);


                    //var legacyCustomers = _dbContext.Customer.GetChangesSinceVersion(0);

                    //foreach (var legacyCustomer in legacyCustomers)
                    //{
                    //    _logger.LogInformation("{@customer}", legacyCustomer);

                    //    var customer =
                    //        _inventoryDbContext.Customers.FirstOrDefault(c => c.Code == legacyCustomer.CustomerCode) ??
                    //        _inventoryDbContext.Customers.Add(new Customer()).Entity;

                    //    customer.Name = legacyCustomer.Name;
                    //    customer.Code = legacyCustomer.CustomerCode;
                    //}

                    //_inventoryDbContext.SaveChanges();
                };

                _sqlTableDependencies.Add(sqlTableDependency);

                _logger.LogInformation("Sql Change Tracking Engine configured for Entity: {entityTypeName}", entityType.Name);
            }

            _sqlTableDependencies.ForEach(s => s.Start());

            return Task.CompletedTask;
        }

        public void Stop()
        {
            _sqlTableDependencies.ForEach(s => s.Dispose());
        }

        public void Dispose()
        {
            Stop();
        }
    }

    
}
