using System;
using System.IO;
using System.Net.Http;
using CDJ.Config;
using CDJ.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var serverConfig = config.Get<ServerConfig>()!;
        var path = serverConfig.LogPath.Replace("{time}", DateTime.Now.ToString("yyyy_dd_MM[hh-mm]"));
        
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
                collection.AddSingleton<SocketService>(); 
                collection.AddSingleton<OneBotService>();
                
                collection.AddSingleton<ICDJService>(n => n.GetRequiredService<SocketService>());
                collection.AddSingleton<ICDJService>(n => n.GetRequiredService<OneBotService>());

                if (config.Get<ServerConfig>()!.EAC)
                {
                    collection.AddSingleton<EACService>();
                    collection.AddSingleton<ICDJService>(n => n.GetRequiredService<EACService>());
                }
                
                collection.AddSingleton<RoomsService>();
                collection.AddSingleton<EnvironmentalTextService>();
                collection.AddTransient<HttpClient>();
                collection.Configure<ServerConfig>(config);
            })
            .UseSerilog();
        return hostBuilder;
    }
}