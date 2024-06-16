using CDJ.Config;
using Microsoft.Extensions.Options;

namespace CDJ.Services;

public class CDJService
(
    IServiceProvider _serviceProvider,
    ILogger<CDJService> logger,
    IOptions<ServerConfig> _options) : IHostedService
{
    private readonly ServerConfig _config = _options.Value;
    public async Task StartAsync(CancellationToken cancellationToken)
    {

        var services = _serviceProvider.GetServices<ICDJService>();
        foreach (var service in services)
        {
            try
            {
                await service.StartAsync(_config, this, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError($"Start Error {service.GetType().Name} {e}");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var services = _serviceProvider.GetServices<ICDJService>();
        foreach (var service in services)
        {
            try
            {
                await service.StopAsync();
            }
            catch (Exception e)
            {
                logger.LogError($"Stop Error {service.GetType().Name} {e}");
            }
        }
    }
}