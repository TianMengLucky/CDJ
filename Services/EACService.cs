using System.Net;
using System.Net.Sockets;
using System.Text;
using CDJ.CDJData;
using CDJ.Config;

namespace CDJ.Services;

public class EACService(ILogger<EACService> logger, OneBotService oneBotService) : ICDJService
{
    private Stream _stream = null!;
    private StreamWriter _writer = null!;

    public TcpListener _TcpListener = null!;
    public ServerConfig _Config = null!;
    public IPAddress Address => IPAddress.Parse(_Config.Ip);
    public Task? _Task;
    public readonly List<Socket> _Sockets = [];
    public List<EACData> _EacDatas = [];
    
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
        await oneBotService.SendMessage(
            $"AddBan\nName:{data.Name}\nFriendCode:{data.FriendCode}Reason:{data.Reason}Count:{data.Count}");
    }

    public ValueTask StartAsync(ServerConfig config, CDJService cdjService, CancellationToken cancellationToken)
    {
        _Config = config;
        _stream = File.Open(_Config.EACPath, FileMode.OpenOrCreate, FileAccess.Write);
        _writer = new StreamWriter(_stream)
        {
            AutoFlush = true
        };
        
        logger.LogInformation("CreateEACSocket");
        _TcpListener = new TcpListener(Address, _Config.EACPort);
        _TcpListener.Start();
        logger.LogInformation($"Start EAC:{_Config.Ip} {_Config.EACPort}");
        cancellationToken.Register(() => _TcpListener.Dispose());
        _Task = Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var socket = await _TcpListener.AcceptSocketAsync(cancellationToken);
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
        
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync()
    {
        _writer.Close();
        _stream.Close();
        _Task?.Dispose();
        foreach (var so in _Sockets)
            so.Dispose();
        return ValueTask.CompletedTask;
    }
}