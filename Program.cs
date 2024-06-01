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
            .AddJsonFile("Config.json", true)
            .Build();
        
        hostBuilder
            .ConfigureAppConfiguration(builder => builder.AddConfiguration(config))
            .ConfigureServices((context, collection) => 
            { 
                collection.AddHostedService<CDJService>(); 
                collection.AddSingleton<SocketService>(); 
                collection.AddSingleton<OneBotService>();
                collection.AddSingleton<RoomsService>();
                collection.Configure<ServerConfig>(config.GetSection("Server"));
            })
            .UseSerilog();
        return hostBuilder;
    }
}

public class CDJService(SocketService socketService, OneBotService oneBotService) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!socketService.CreateSocket())
            return Task.FromException(new Exception("Failed to create socket"));
        
        if (!oneBotService.ConnectBot().Result)
            return Task.FromException(new Exception("Failed to Connect Bot"));
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        socketService.Stop();
        oneBotService.Stop();
        return Task.CompletedTask;
    }
}

public class SocketService(ILogger<SocketService> logger, RoomsService roomsService, OneBotService oneBotService, IOptions<ServerConfig> config)
{
    public TcpListener _TcpListener = null!;
    
    private readonly ServerConfig _config = config.Value;

    public IPAddress Address => IPAddress.Parse(_config.Ip);
    public Task? _Task;
    private readonly CancellationTokenSource _cancellationToken = new();
    
    public bool CreateSocket()
    {
        logger.LogInformation("CreateSocket");
        _TcpListener = new TcpListener(Address, _config.Port);
        _TcpListener.Start();
        _cancellationToken.Token.Register(() => _TcpListener.Dispose());
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                using var socket = await _TcpListener.AcceptSocketAsync();
                var bytes = new byte[2_048];
                await socket.ReceiveAsync(bytes);
                var str = Encoding.Default.GetString(bytes);
                logger.LogInformation($"sokcet {_config.Ip} {_config.Port} {str}");
                if (!roomsService.TryGetRoom(str, out var room))
                {
                    logger.LogInformation("GetRoom No");
                    continue;
                }
                var message = roomsService.ParseRoom(room);
                if (_config.SendToQun)
                    await oneBotService.SendMessageToQun(message, _config.QQID);
                else
                    await oneBotService.SendMessageToLXR(message, _config.QQID);
            }
        }, TaskCreationOptions.LongRunning);
        return true;
    }

    public async void Stop()
    {
        await _cancellationToken.CancelAsync();
        _Task?.Dispose();
    }
}

public class OneBotService(ILogger<OneBotService> logger, IOptions<ServerConfig> config)
{
    public readonly HttpClient _Client = new();
    private readonly ServerConfig _config = config.Value;
    public bool ConnectIng;
    
    public async Task<bool> ConnectBot()
    {
        var get = await _Client.GetAsync($"{_config.BotHttpUrl}/get_login_info");
        if (!get.IsSuccessStatusCode)
            return false;

        logger.LogInformation(await get.Content.ReadAsStringAsync());
        ConnectIng = true;
        
        return true;
    }

    public async Task SendMessageToQun(string message, long id)
    {
        if (!ConnectIng) await ConnectBot();
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
        if (!ConnectIng) await ConnectBot();
        var jsonString = JsonSerializer.Serialize(new UserMessage
        {
            message = message,
            user_id = id.ToString()
        });
        await _Client.PostAsync($"{_config.BotHttpUrl}/send_private_msg", new StringContent(jsonString));
        logger.LogInformation($"Send To User id:{id} message:{message}");
    }
    
    public void Stop()
    {
        _Client.Dispose();
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
        if (!Version.TryParse(strings[1], out var version))
            return false;

        var count = int.Parse(strings[2]);
        var langId = (LangName)byte.Parse(strings[3]);
        var serverName = strings[4];
        var playName = strings[5];

        room = new Room(code, version, count, langId, serverName, playName);
        _Rooms.Add(room);
        return true;
    }


    public string ParseRoom(Room room)
    { 
        return 
$@"Code:{room.Code}
Version:{room.Version}
Count:{room.Count}
Lang:{Enum.GetName(room.LangId)}
Server:{room.ServerName}
Player:{room.PlayerName}
";
    }
}

public record Room(string Code, Version Version, int Count, LangName LangId, string ServerName, string PlayerName);

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

public class ServerConfig
{
    public string Ip { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 25000;

    public string BotHttpUrl { get; set; } = "http://localhost:3000";
    public bool SendToQun { get; set; } = false;
    public long QQID { get; set; } = 2133404320;
}

