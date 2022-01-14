using System;
using System.Threading;
using System.Threading.Tasks;
using Elsa.MultiTenancy;
using Elsa.Retention.Jobs;
using Elsa.Retention.Options;
using Elsa.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Retention.HostedServices
{
    public class MultitenantCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IDistributedLockProvider _distributedLockProvider;
        private readonly ILogger<MultitenantCleanupService> _logger;
        private readonly TimeSpan _interval;
        private readonly ITenantStore _tenantStore;

        public MultitenantCleanupService(IOptions<CleanupOptions> options, IServiceScopeFactory serviceScopeFactory, IDistributedLockProvider distributedLockProvider, ILogger<MultitenantCleanupService> logger, ITenantStore tenantStore)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _distributedLockProvider = distributedLockProvider;
            _logger = logger;
            _interval = options.Value.SweepInterval.ToTimeSpan();
            _tenantStore = tenantStore;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var tenant in _tenantStore.GetTenants())
            {
                using var scope = _serviceScopeFactory.CreateScopeForTenant(tenant);
                var job = scope.ServiceProvider.GetRequiredService<CleanupJob>();

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_interval, stoppingToken);
                    await using var handle = await _distributedLockProvider.AcquireLockAsync(nameof(CleanupService), cancellationToken: stoppingToken);

                    if (handle == null)
                        continue;

                    try
                    {
                        await job.ExecuteAsync(stoppingToken);
                    }
                    catch (Exception e)
                    {
                        var tenantName = tenant.Name ?? tenant.Prefix;
                        _logger.LogError(e, "Failed to perform cleanup for tenant {Tenant} this time around. Next cleanup attempt will happen in {Interval}", tenantName, _interval);
                    }
                }
            }
        }
    }
}