using System.Threading;
using System.Threading.Tasks;
using CDJ.Config;

namespace CDJ.Services;

public interface ICDJService
{
    public ValueTask StartAsync(ServerConfig config, CDJService cdjService, CancellationToken cancellationToken);
    public ValueTask StopAsync();
}