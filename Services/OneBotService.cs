using System.Text.Json;
using CDJ.CDJData;
using CDJ.Config;

namespace CDJ.Services;

public class OneBotService(ILogger<OneBotService> logger, HttpClient _Client) : ICDJService
{
    private ServerConfig _config = null!;
    public readonly List<(long, bool)> _Reads = [];
    public Task? _Task;
    public Queue<SendInfo> SendInfos = [];
    
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

    public ValueTask SendMessage(string message)
    {
        var info = new SendInfo(message);
        if (_config.QQID != 0)
        {
            info.SendTargets.Add((_config.SendToGroup, _config.QQID));
        }
        else
        {
            foreach (var (id, isGroup) in _Reads)
            {
                info.SendTargets.Add((isGroup, id));
            }
        }
        SendInfos.Enqueue(info);
        return ValueTask.CompletedTask;
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
        _config = config;
        try
        {
            await Read();
        }
        catch
        {
            logger.LogError("Read Error");
        }

        await SendBotInfo();

        var span = TimeSpan.FromSeconds(_config.MessageInterval);
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (SendInfos.TryDequeue(out var info))
                {
                    foreach (var (isGroup, qqId) in info.SendTargets)
                    {
                        try
                        {
                            if (isGroup)
                            {
                                await SendMessageToGroup(info.Message, qqId);
                            }
                            else
                            {
                                await SendMessageToUser(info.Message, qqId);
                            }
                        }
                        catch
                        {
                            logger.LogWarning($"SendInfo Error IsGroup:{isGroup} QQID:{qqId} Meesage:{info.Message}");
                            goto End;
                        }
                    }
                }
                End:
                
                Thread.Sleep(span);
            }
        },TaskCreationOptions.LongRunning);
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
        _Task?.Dispose();
        return ValueTask.CompletedTask;
    }
}