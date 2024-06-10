using Discord;
using Discord.WebSocket;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.RegularExpressions;

namespace TONEX_CHAN;

public static class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().
            WriteTo.Console()
            .WriteTo.File("log.txt")
            .CreateBootstrapLogger();
        
        try
        {
            Create(args).Build().Run();
            Log.Logger.Information("Run successfully");
        }
        catch (Exception e)
        {
            Log.Logger.Error($"Run Error: \n {e}");
        } 
    }

    public static IHostBuilder Create(string[] args)
    {
        try
        {
            var hostBuilder = Host.CreateDefaultBuilder(args).UseContentRoot(Directory.GetCurrentDirectory());
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();

            hostBuilder
                .ConfigureAppConfiguration(builder => builder.AddConfiguration(config))
                .ConfigureServices((context, collection) =>
                {
                    collection.AddHostedService<TONEX_CHANService>();
                    collection.AddSingleton<SocketService>();
                    collection.AddSingleton<OneBotService>();
                //    collection.AddSingleton<DiscordBotService>();
                    collection.AddSingleton<RoomsService>();
                    collection.AddSingleton<EACService>();
                    collection.AddSingleton<ActiveService>();
                    collection.AddScoped<HttpClient>();
                    collection.Configure<ServerConfig>(config);
                })
                .UseSerilog();

            return hostBuilder; 
        }
        catch (Exception e)
        {
            Log.Logger.Error($"Run Error: \n {e}");
          
            throw;
        }
    }
}

public class TONEX_CHANService
(
    ILogger<TONEX_CHANService> logger,
    SocketService socketService, 
    OneBotService oneBotService,
    DiscordBotService discordBotService,
    EACService eacService,
    ActiveService activeService) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        
        
        try
        {
            if (!socketService.CreateSocket())
                logger.LogError("Failed to create socket");
        }
        catch
        {
            // ignored
        }
        
        try
        {
            if (!eacService.CreateSocket())
                logger.LogError("Failed to CreateEAC");
        }
        catch
        {
            // ignored
        }

        try
        {
            if (!await oneBotService.ConnectBot())
                logger.LogError("Failed to Connect Bot");
            await oneBotService.Read();
        }
        catch
        {
            // ignored
        }

        try
        {
            await activeService.StartAsync();
        }
        catch
        {
            // ignored
            logger.LogError("Start Error Active Service");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await socketService.Stop();
        await oneBotService.Stop();
        await discordBotService.Stop();
        await eacService.Stop();
        await activeService.StopAsync();
    }
}

public class SocketService(ILogger<SocketService> logger, RoomsService roomsService, OneBotService oneBotService, DiscordBotService discordBotService, IOptions<ServerConfig> config)
{
    public TcpListener _TcpListener = null!;
    
    private readonly ServerConfig _config = config.Value;

    public IPAddress Address => IPAddress.Parse(_config.Ip);
    public Task? _Task;
    private readonly CancellationTokenSource _cancellationToken = new();

    public List<Socket> _Sockets = [];
    
    public bool CreateSocket()
    {
        logger.LogInformation("CreateSocket");
        _TcpListener = new TcpListener(Address, _config.Port);
        _TcpListener.Start();
        logger.LogInformation($"Start :{_config.Ip} {_config.Port} {_config.SendToQQ_Group} {_config.BotHttpUrl} {_config.QQID}");
        _cancellationToken.Token.Register(() => _TcpListener.Dispose());
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var socket = await _TcpListener.AcceptSocketAsync();
                    var bytes = new byte[60];
                    var count =await socket.ReceiveAsync(bytes);
                    var str = Encoding.Default.GetString(bytes).TrimEnd('\0');
                    logger.LogInformation($"sokcet {_config.Ip} {_config.Port} {str}");
                    
                    if (str == "test")
                    {
                        await socket.SendAsync(Encoding.Default.GetBytes("Test Form SERVER"));
                        continue;
                    }

                    roomsService.TryGetRoom(str, out var room);
                    try
                    {
                        var message_QQ = roomsService.ParseRoom_QQ(room);
                        await oneBotService.SendMessage(message_QQ);
                    }
                    catch { }
                    try
                    {
                        var message_DC = roomsService.ParseRoom_DC(room);
                        await discordBotService.SendMessage(message_DC);
                    }
                    catch { }
                }
                catch (Exception e)
                {
                    logger.LogError(e.ToString());
                }
            }
        }, TaskCreationOptions.LongRunning);
        return true;
    }

    public async Task Stop()
    {
        await _cancellationToken.CancelAsync();
        _Task?.Dispose();
    }
}

public class OneBotService(ILogger<OneBotService> _logger, IOptions<ServerConfig> config, HttpClient _client)
{
    private readonly ServerConfig _config = config.Value;
    public bool ConnectIng;
    public List<(long, bool)> _reads = [];

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
            _reads.Add((long.Parse(str[0]), bool.Parse(str[1])));
        }
    }

    public async Task<bool> ConnectBot()
    {
        var get = await _client.GetAsync($"{_config.BotHttpUrl}/get_login_info");
        if (!get.IsSuccessStatusCode)
            return false;

        _logger.LogInformation(await get.Content.ReadAsStringAsync());
        ConnectIng = true;

        return true;
    }

    public async Task SendMessage(string message)
    {
        if (!ConnectIng)
            await ConnectBot();
        if (_config.QQID != 0)
        {
            if (_config.SendToQQ_Group)
            {
                await SendMessageToQQ_Group(message, _config.QQID);
            }
            else
            {
                await SendMessageToQQ_ContactPerson(message, _config.QQID);
            }
        }
        else
        {
            foreach (var (id, isQQ_Group) in _reads)
            {
                if (isQQ_Group)
                {
                    await SendMessageToQQ_Group(message, id);
                }
                else
                {
                    await SendMessageToQQ_ContactPerson(message, id);
                }
            }
        }
    }

    public async Task SendMessageToQQ_Group(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new GroupMessage
        {
            message = message,
            group_id = id.ToString()
        });
        await _client.PostAsync($"{_config.BotHttpUrl}/send_group_msg", new StringContent(jsonString));
        _logger.LogInformation($"Send To Group id:{id} message:{message}");
    }

    public async Task SendMessageToQQ_ContactPerson(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new UserMessage
        {
            message = message,
            user_id = id.ToString()
        });
        await _client.PostAsync($"{_config.BotHttpUrl}/send_private_msg", new StringContent(jsonString));
        _logger.LogInformation($"Send To User id:{id} message:{message}");
    }

    public Task Stop()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

}

public class DiscordBotService
{
    private readonly ILogger<DiscordBotService> _logger;
    private readonly DiscordSocketClient _client;
    private readonly List<ulong> _channelIds = new();

    public DiscordBotService(ILogger<DiscordBotService> logger, DiscordSocketClient client)
    {
        _logger = logger;
        _client = client;
    }

    public bool ConnectIng;
    public async Task<bool> ConnectBot()
    {
        try
        {
            if (_client == null)
            {
                _logger.LogError("Error: Client is not initialized.");
                return false;
            }
            string token = "S93tx9SWr6wARS9T1CLnlfRIRaoNwZ_O";
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            _logger.LogInformation("Connected to Discord bot successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to connect to Discord bot: {ex.Message}");
            return false;
        }
    }

    public async Task SendMessage(string message)
    {
        if (!ConnectIng)
        {
            if (!await ConnectBot())
            {
                return;
            }
        }

        if (_client == null)
        {
            _logger.LogError("Error: Client is not initialized.");
            return;
        }

        foreach (var guild in _client.Guilds)
        {
            foreach (var channelId in _channelIds.ToList())  // 使用ToList()复制一份集合进行循环
            {
                var targetChannel = guild.GetChannel(channelId) as ISocketMessageChannel;
                if (targetChannel != null)
                {
                    await targetChannel.SendMessageAsync(message);
                    _logger.LogInformation($"Sent message to channel id: {channelId}");
                }
            }
        }
    }

    public Task Stop()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }
}

public class Login_Info
{
    public long user_id { get; set; }
    public string nickname { get; set; }
}

public abstract class SendMessage
{
    public abstract string message_type { get; }
    
    public string message { get; set; }
}

public class GroupMessage : SendMessage
{
    public override string message_type { get; } = "group";
    public string group_id { get; set; }
}

public class UserMessage : SendMessage
{
    public override string message_type { get; } = "private";
    public string user_id { get; set; }
}

public class RoomsService
{
    public readonly List<Room> _Rooms = [];
    
    public bool TryGetRoom(string text,[MaybeNullWhen(false)] out Room room)
    {
        room = null;
        var strings = text.Split('|');
        if (strings.Length < 6) return false;
        
        var code = strings[0];
        if (code.Length != 6) return false;

        var BuildVersion = "";
        string version = strings[1];

        bool isValidVersion = IsVersionValid(version);
        if (isValidVersion) return false;

        string input = strings[2]; 
        string pattern = @"^\d+$";
        if (!Regex.IsMatch(input, pattern)) return false;
        var count = int.Parse(strings[2]);

        var langId = Enum.Parse<LangName>(strings[3]);
        var serverName = strings[4];
        var playName = strings[5];
        
        room = new Room(code, version, count, langId, serverName, playName, BuildVersion);
        _Rooms.Add(room);
        return true;
    }
    static bool IsVersionValid(string version)
    {
        string pattern = @"^\d+\.\d+_\d{8}_(Debug|Canary|Dev|Preview)(?:_\d+)?$";
        Regex regex = new (pattern);
        return regex.IsMatch(version);
    }

    public string ParseRoom_QQ(Room room)
    {
        var ln = lang_forZh.TryGetValue(room.LangId, out var value) ? value : Enum.GetName(room.LangId);
        var ver = room.Version == null ? room.BuildId : room.Version.ToString();
        var def = $@"房间号: {room.Code}
版本号: {ver}
人数: {room.Count}
语言: {ln}
服务器: {room.ServerName}
房主: {room.PlayerName}
"; ;
        return def;
    }

    public static readonly Dictionary<LangName, string> lang_forZh = new()
    {
        { LangName.English, "英语" },
    { LangName.Latam, "拉丁美洲" },
    { LangName.Brazilian, "巴西" },
    { LangName.Portuguese, "葡萄牙" },
    { LangName.Korean, "韩语" },
    { LangName.Russian, "俄语" },
    { LangName.Dutch, "荷兰语" },
    { LangName.Filipino, "菲律宾语" },
    { LangName.French, "法语" },
    { LangName.German, "德语" },
    { LangName.Italian, "意大利语" },
    { LangName.Japanese, "日语" },
    { LangName.Spanish, "西班牙语" },
    { LangName.SChinese, "简体中文" },
    { LangName.TChinese, "繁体中文" },
    { LangName.Irish, "爱尔兰语" }
    };

    public string ParseRoom_DC(Room room)
    {
        var ln = lang_forEn.TryGetValue(room.LangId, out var value) ? value : Enum.GetName(room.LangId);
        var ver = room.Version == null ? room.BuildId : room.Version.ToString();
        var def = $@"Room Code: {room.Code}
Version: {ver}
People: {room.Count}
Language: {ln}
Server: {room.ServerName}
Host: {room.PlayerName}
"; ;
        return def;
    }

    public static readonly Dictionary<LangName, string> lang_forEn = new()
    {
      
    { LangName.English, "English" },
    { LangName.Latam, "Latam" },
    { LangName.Brazilian, "Brazilian" },
    { LangName.Portuguese, "Portuguese" },
    { LangName.Korean, "Korean" },
    { LangName.Russian, "Russian" },
    { LangName.Dutch, "Dutch" },
    { LangName.Filipino, "Filipino" },
    { LangName.French, "French" },
    { LangName.German, "German" },
    { LangName.Italian, "Italian" },
    { LangName.Japanese, "Japanese" },
    { LangName.Spanish, "Spanish" },
    { LangName.SChinese, "SChinese" },
    { LangName.TChinese, "TChinese" },
    { LangName.Irish, "Irish" }

};
}

public record Room(string Code, string Version, int Count, LangName LangId, string ServerName, string PlayerName, string BuildId = "");

public enum LangName : byte
{
    English,
    Latam,
    Brazilian,
    Portuguese,
    Korean,
    Russian,
    Dutch,
    Filipino,
    French,
    German,
    Italian,
    Japanese,
    Spanish,
    SChinese,
    TChinese,
    Irish
}


public class EACService
{
    private readonly ServerConfig _Config;
    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly ILogger<EACService> logger;
    private readonly OneBotService _oneBotService;

    public EACService(IOptions<ServerConfig> options, ILogger<EACService> logger, OneBotService oneBotService)
    {
        _Config = options.Value;
        _stream = File.Open(_Config.EACPath, FileMode.OpenOrCreate, FileAccess.Write);
        _writer = new StreamWriter(_stream)
        {
            AutoFlush = true
        };
        this.logger = logger;
        _oneBotService = oneBotService;
    }
    
    public TcpListener _TcpListener = null!;
    public IPAddress Address => IPAddress.Parse(_Config.Ip);
    public Task? _Task;
    private readonly CancellationTokenSource _cancellationToken = new();
    public List<Socket> _Sockets = [];

    public List<EACData> _EacDatas = [];
    
    public bool CreateSocket()
    {
        logger.LogInformation("CreateEACSocket");
        _TcpListener = new TcpListener(Address, _Config.EACPort);
        _TcpListener.Start();
        logger.LogInformation($"Start EAC:{_Config.Ip} {_Config.EACPort}");
        _cancellationToken.Token.Register(() => _TcpListener.Dispose());
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var socket = await _TcpListener.AcceptSocketAsync();
                    _Sockets.Add(socket);
                    var bytes = new byte[60];
                    await socket.ReceiveAsync(bytes);
                    var str = Encoding.Default.GetString(bytes).TrimEnd('\0');
                    logger.LogInformation($"sokcet {_Config.Ip} {_Config.EACPort} {str}");
                    if (str == "test")
                    {
                        await socket.SendAsync(Encoding.Default.GetBytes("Test Form SERVER"));
                        continue;
                    }

                    var data = GET(str, out var clientId, out var name, out var reason);
                    if (data != null)
                    {
                        data.Count++;
                        data.ClientId = clientId;
                        data.Name = name;
                        data.Reason = reason;
                    }
                    else
                    {
                        data = EACData.Get(str);
                        _EacDatas.Add(data);
                        if (data.Count > _Config.EACCount)
                            await Ban(data);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e.ToString());
                }
            }
        }, TaskCreationOptions.LongRunning);
        return true;
    }

    public async Task Stop()
    {
        await _cancellationToken.CancelAsync();
        _writer.Close();
        _stream.Close();
        _Task?.Dispose();
        foreach (var so in _Sockets)
            so.Dispose();
    }
    public EACData? GET(string s, out int clientId, out string name, out string reason)
    {
        var strings = s.Split('|');
        clientId = int.Parse(strings[0]);
        name = strings[2];
        reason = strings[3];
        return _EacDatas.FirstOrDefault(n => n.FriendCode == strings[1]);
    }

    public async Task Ban(EACData data)
    {
        data.Ban = true;
        await _writer.WriteLineAsync($"Id:{data.ClientId}FriendCode:{data.FriendCode}Name:{data.Name}Reason:{data.Reason} : Count{data.Count}");
        await _oneBotService.SendMessage(
            $"AddBan\nName:{data.Name}\nFriendCode:{data.FriendCode}Reason:{data.Reason}Count:{data.Count}");
    }
}

public class EACData
{
    public int ClientId { get; set; }
    public string FriendCode { get; init; }
    public string Name { get; set; }
    public string Reason { get; set; }

    public int Count;
    public bool Ban;
    
    public static EACData Get(string s)
    {
        var strings = s.Split('|');
        return new EACData
        {
            ClientId = int.Parse(strings[0]),
            FriendCode = strings[1],
            Name = strings[2],
            Reason = strings[3]
        };
    }
}

public class ServerConfig
{
    public string Ip { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 25000;

    public string BotHttpUrl { get; set; } = "http://localhost:3000";
    public bool SendToQQ_Group { get; set; } = false;
    public long QQID { get; set; }
    public int EACPort { get; set; } = 25250;
    public int EACCount { get; set; } = 5;
    public string EACPath { get; set; } = "./EAC.txt";

    public string ReadPath = string.Empty;
    public string ApiUrl = string.Empty;
    public int Time = 30;
}

public class ActiveService(ILogger<ActiveService> _logger, IOptions<ServerConfig> _options, HttpClient _client)
{
    public Task? _Task;
    private readonly CancellationTokenSource _source = new();
    public ValueTask StartAsync()
    {
        if(_options.Value.ApiUrl == string.Empty) return ValueTask.CompletedTask;
        _source.Token.Register(() => _Task?.Dispose());
        var time = TimeSpan.FromSeconds(30);
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!_source.IsCancellationRequested)
            {
                try
                {
                    var responseMessage = await _client.GetAsync(_options.Value.ApiUrl);
                    _logger.LogInformation($"Get{_options.Value.ApiUrl} {responseMessage.StatusCode} {await responseMessage.Content.ReadAsStringAsync()}");
                    Thread.Sleep(time);
                }
                catch
                {
                    // ignored
                }
            }
        }, TaskCreationOptions.LongRunning);
        return ValueTask.CompletedTask;
    }
    
    public async ValueTask StopAsync()
    {
        await _source.CancelAsync();
    }
}

