
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;

namespace CDJ;

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
        var hostBuilder = Host.CreateDefaultBuilder(args).UseContentRoot(Directory.GetCurrentDirectory());
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json")
            .Build();
        
        hostBuilder
            .ConfigureAppConfiguration(builder => builder.AddConfiguration(config))
            .ConfigureServices((context, collection) => 
            { 
                collection.AddHostedService<CDJService>(); 
                collection.AddSingleton<SocketService>(); 
                collection.AddSingleton<OneBotService>();
                collection.AddSingleton<RoomsService>();
                collection.AddSingleton<EACService>();
                collection.AddSingleton<ActiveService>();
                collection.AddScoped<HttpClient>();
                collection.Configure<ServerConfig>(config);
            })
            .UseSerilog();
        return hostBuilder;
    }
}

public class CDJService
(
    ILogger<CDJService> logger,
    SocketService socketService, 
    OneBotService oneBotService,
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
        await eacService.Stop();
        await activeService.StopAsync();
    }
}

public class SocketService(ILogger<SocketService> logger, RoomsService roomsService, OneBotService oneBotService, IOptions<ServerConfig> config)
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
        logger.LogInformation($"Start :{_config.Ip} {_config.Port} {_config.SendToQun} {_config.BotHttpUrl} {_config.QQID}");
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
                    var message = roomsService.ParseRoom(room);
                    await oneBotService.SendMessage(message);
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

public class OneBotService(ILogger<OneBotService> logger, IOptions<ServerConfig> config, HttpClient _Client)
{
    private readonly ServerConfig _config = config.Value;
    public bool ConnectIng;
    public List<(long, bool)> _Reads = [];


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
    
    public async Task<bool> ConnectBot()
    {
        var get = await _Client.GetAsync($"{_config.BotHttpUrl}/get_login_info");
        if (!get.IsSuccessStatusCode)
            return false;

        logger.LogInformation(await get.Content.ReadAsStringAsync());
        ConnectIng = true;
        
        return true;
    }

    public async Task SendMessage(string message)
    {
        if (!ConnectIng) 
            await ConnectBot();
        if (_config.QQID != 0)
        {
            if (_config.SendToQun)
            {
                await SendMessageToQun(message, _config.QQID);
            }
            else
            {
                await SendMessageToLXR(message, _config.QQID);
            }
        }
        else
        {
            foreach (var (id, isQun) in _Reads)
            {
                if (isQun)
                {
                    await SendMessageToQun(message, id);
                }
                else
                {
                    await SendMessageToLXR(message, id);
                }
            }
        }
    }

    public async Task SendMessageToQun(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new GroupMessage
        {
            message = message,
            group_id = id.ToString()
        });
        await _Client.PostAsync($"{_config.BotHttpUrl}/send_group_msg", new StringContent(jsonString));
        logger.LogInformation($"Send To Group id:{id} message:{message}");
    }
    
    public async Task SendMessageToLXR(string message, long id)
    {
        var jsonString = JsonSerializer.Serialize(new UserMessage
        {
            message = message,
            user_id = id.ToString()
        });
        await _Client.PostAsync($"{_config.BotHttpUrl}/send_private_msg", new StringContent(jsonString));
        logger.LogInformation($"Send To User id:{id} message:{message}");
    }
    
    public Task Stop()
    {
        _Client.Dispose();
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
        if (strings.Length < 6)
            return false;
        
        var code = strings[0];
        var BuildVersion = "";
        Version? version = null;
        if (Version.TryParse(strings[1], out var v))
            version = v;
        else
            BuildVersion = strings[1];

        var count = int.Parse(strings[2]);
        var langId = Enum.Parse<LangName>(strings[3]);
        var serverName = strings[4];
        var playName = strings[5];
        
        room = new Room(code, version, count, langId, serverName, playName, BuildVersion);
        _Rooms.Add(room);
        return true;
    }


    public string ParseRoom(Room room)
    {
        var ln = lang.TryGetValue(room.LangId, out var value) ? value : Enum.GetName(room.LangId);
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

    public static readonly Dictionary<LangName, string> lang = new()
    {
        {LangName.SChinese, "简体中文"},
        { LangName.TChinese , "繁体中文"}
    };
}

public record Room(string Code, Version? Version, int Count, LangName LangId, string ServerName, string PlayerName, string BuildId = "");

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
    public bool SendToQun { get; set; } = false;
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

