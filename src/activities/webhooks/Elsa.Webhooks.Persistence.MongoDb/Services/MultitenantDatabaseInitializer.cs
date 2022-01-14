using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elsa.MultiTenancy;
using Elsa.Services;
using Elsa.Webhooks.Models;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Elsa.Webhooks.Persistence.MongoDb.Services
{
    public class MultitenantDatabaseInitializer : IStartupTask
    {
        private readonly ITenantStore _tenantStore;
        private readonly IServiceScopeFactory _scopeFactory;

        public MultitenantDatabaseInitializer(IServiceScopeFactory scopeFactory, ITenantStore tenantStore)
        {
            _scopeFactory = scopeFactory;
            _tenantStore = tenantStore;
        }

        public int Order => 0;

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            foreach (var tenant in _tenantStore.GetTenants())
            {
                using var scope = _scopeFactory.CreateScopeForTenant(tenant);

                var dbContextProvider = scope.ServiceProvider.GetRequiredService<MultitenantElsaMongoDbContextProvider>();

                await CreateWebhookDefinitionsIndexes(dbContextProvider, cancellationToken);
            }
        }

        private async Task CreateWebhookDefinitionsIndexes(MultitenantElsaMongoDbContextProvider dbContextProvider, CancellationToken cancellationToken)
        {
            var builder = Builders<WebhookDefinition>.IndexKeys;
            var tenantKeysDefinition = builder.Ascending(x => x.TenantId);
            var nameKeysDefinition = builder.Ascending(x => x.Name);
            var pathKeysDefinition = builder.Ascending(x => x.Path);
            var payloadKeysDefinition = builder.Ascending(x => x.PayloadTypeName);
            await CreateIndexesAsync(dbContextProvider.WebhookDefinitions, cancellationToken, tenantKeysDefinition, nameKeysDefinition, pathKeysDefinition, payloadKeysDefinition);
        }

        private async Task CreateIndexesAsync<T>(IMongoCollection<T> collection, CancellationToken cancellationToken, params IndexKeysDefinition<T>[] definitions)
        {
            var models = definitions.Select(x => new CreateIndexModel<T>(x));
            await collection.Indexes.CreateManyAsync(models, cancellationToken);
        }
    }
}