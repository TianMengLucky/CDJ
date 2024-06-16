using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CDJ.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CDJ.Services;

public class CDJService
(
    IServiceProvider _serviceProvider,
    ILogger<CDJService> logger,
    IOptions<ServerConfig> _options) : IHostedService
{
    private readonly ServerConfig _config = _options.Value;
    private List<IPAddress> _BanIp = [];
    public IReadOnlyList<IPAddress> BanIps => _BanIp;

    public async Task ReadBanIp()
    {
        if (_config.BanIpPath == string.Empty)
            return;

        if (!File.Exists(_config.BanIpPath))
            return;

        try
        {
            await using var stream = File.OpenRead(_config.BanIpPath);
            using var Reader = new StreamReader(stream);
            while (Reader.EndOfStream)
            {
                var str = await Reader.ReadLineAsync();
                if (IPAddress.TryParse(str, out var ip))
                {
                    _BanIp.Add(ip);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReadBanIp();
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