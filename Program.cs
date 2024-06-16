using CDJ.Config;
using CDJ.Services;
using Serilog;

namespace CDJ;

public static class Program
{
    public static readonly Version version = new(1, 0, 0);
    
    public static void Main(string[] args)
    {
        var config = CreateConfig();
        SetLog(config);
        
        Log.Logger.Information($"Start Run CDJ Version{version}");
        try
        {
            Create(args, config).Build().Run();
            Log.Logger.Information("Run successfully");
        }
        catch (Exception e)
        {
            Log.Logger.Error($"Run Error: \n {e}");
        } 
    }

    public static void SetLog(IConfiguration config)
    {
        var serverConfig = config.GetSection(ServerConfig.Section).Get<ServerConfig>()!;
        var path = serverConfig.LogPath.Replace("{time}", DateTime.Now.ToString("yyyy_ddd_MM_hh_mm"));
        
        Log.Logger = new LoggerConfiguration().
            WriteTo.Console()
            .WriteTo.File(path)
            .CreateBootstrapLogger();
    }

    public static IConfiguration CreateConfig()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json")
            .Build();
        return config;
    }

    private static IHostBuilder Create(string[] args, IConfiguration config)
    {
        var hostBuilder = Host
            .CreateDefaultBuilder(args)
            .UseContentRoot(Directory.GetCurrentDirectory());
        
        hostBuilder
            .ConfigureAppConfiguration(builder => builder.AddConfiguration(config))
            .ConfigureServices((context, collection) => 
            { 
                collection.AddHostedService<CDJService>();
                collection.AddSingleton<ICDJService, EACService>();
                collection.AddSingleton<ICDJService, SocketService>(); 
                collection.AddSingleton<ICDJService, OneBotService>();
                collection.AddSingleton<RoomsService>();
                collection.AddSingleton<EnvironmentalTextService>();
                collection.AddTransient<HttpClient>();
                collection.Configure<ServerConfig>(config.GetSection(ServerConfig.Section));
            })
            .UseSerilog();
        return hostBuilder;
    }
}