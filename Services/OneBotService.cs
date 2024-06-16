using System.Text.Json;
using CDJ.CDJData;
using CDJ.Config;
using Microsoft.Extensions.Options;

namespace CDJ.Services;

public class OneBotService(ILogger<OneBotService> logger, IOptions<ServerConfig> config, HttpClient _Client) : ICDJService
{
    private readonly ServerConfig _config = config.Value;
    public readonly List<(long, bool)> _Reads = [];
    public async Task Read()
    {
        if (_config.ReadPath == string.Empty) return;
        await using var stream = File.Open(_config.ReadPath, FileMode.OpenOrCreate);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) continue;
            var str = line.Replace(" ", string.Empty).Split('|');
            _Reads.Add((long.Parse(str[0]), bool.Parse(str[1])));
        }
    }

    public async ValueTask SendMessage(string message)
    {
        if (_config.QQID != 0)
        {
            if (_config.SendToGroup)
            {
                await SendMessageToGroup(message, _config.QQID);
            }
            else
            {
                await SendMessageToUser(message, _config.QQID);
            }
        }
        else
        {
            foreach (var (id, isQun) in _Reads)
            {
                if (isQun)
                {
                    await SendMessageToGroup(message, id);
                }
                else
                {
                    await SendMessageToUser(message, id);
                }
            }
        }
    }

    public async ValueTask SendMessageToGroup(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new GroupMessage
        {
            message = message,
            group_id = id.ToString()
        });
        await _Client.PostAsync($"{_config.BotHttpUrl}/send_group_msg", new StringContent(jsonString));
        logger.LogInformation($"Send To Group id:{id} message:{message}");
    }
    
    public async ValueTask SendMessageToUser(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new UserMessage
        {
            message = message,
            user_id = id.ToString()
        });
        await _Client.PostAsync($"{_config.BotHttpUrl}/send_private_msg", new StringContent(jsonString));
        logger.LogInformation($"Send To User id:{id} message:{message}");
    }

    public async ValueTask StartAsync(ServerConfig config, CDJService cdjService, CancellationToken cancellationToken)
    {
        try
        {
            await Read();
        }
        catch
        {
            logger.LogError("Read Error");
        }

        await SendBotInfo();
    }
    
    public async ValueTask SendBotInfo()
    {
        var get = await _Client.GetAsync($"{_config.BotHttpUrl}/get_login_info");
        if (!get.IsSuccessStatusCode) return;

        logger.LogInformation("QQBot Info" + await get.Content.ReadAsStringAsync());
    }

    public ValueTask StopAsync()
    {
        _Client.Dispose();
        return ValueTask.CompletedTask;
    }
}