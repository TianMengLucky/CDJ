using System.Diagnostics.CodeAnalysis;
using CDJ.CDJData;
using CDJ.Config;
using Microsoft.Extensions.Options;

namespace CDJ.Services;

public class RoomsService(EnvironmentalTextService service, IOptions<ServerConfig> config, ILogger<RoomsService> logger)
{
    public readonly List<Room> _Rooms = [];

    public bool CheckRoom(Room room)
    {
        var r = _Rooms.FirstOrDefault(n => n.Code == room.Code);
        if (r == null)
            return true;

        var time = DateTime.Now - r.Time;
        if (time.TotalMinutes < config.Value.RoomInterval)
        {
            return false;
        }
        
        
        return true;
    }
    
    public bool TryPareRoom(string text,[MaybeNullWhen(false)] out Room room)
    {
        room = null;
        try
        {
            var strings = text.Split('|');
            if (strings.Length < 6)
                return false;
        
            var code = strings[0];
            if (code.Length is not 4 and not 6)
                return false;

            room = _Rooms.FirstOrDefault(n => n.Code == code);
            
            if (!Version.TryParse(strings[1], out var version))
                return false;
            
            var count = int.Parse(strings[2]);
            if (count > 15)
                return false;

            if (!Enum.TryParse<LangName>(strings[3], out var langId))
                return false;
            
            var serverName = strings[4];
            
            var playName = strings[5];
            
            var BuildVersion = "";
            if (strings.Length > 6)
            {
                BuildVersion = strings[6];
            }

            var has = true;
            if (room == null)
            {
                room = new Room(code, version, count, langId, serverName, playName, BuildVersion);
                has = false;
            }
            
            if (!CheckRoom(room))
            {
                logger.LogInformation("CheckRoom:False");
                return false;
            }
            
            room.Time = DateTime.Now;
            if (!has)
                _Rooms.Add(room);
            
            service
                .Update("RoomCode", code)
                .Update("Version", version.ToString() ?? BuildVersion)
                .Update("PlayerCount", count.ToString())
                .Update("Language", (config.Value.LangTextToCN ? lang[langId] : Enum.GetName(langId))!)
                .Update("ServerName", serverName)
                .Update("PlayerName", playName);
        }
        catch (Exception e)
        {
            logger.LogWarning(e.ToString());
            return false;
        }
        
        return true;
    }
    

    public static readonly Dictionary<LangName, string> lang = new()
    {
        {LangName.SChinese, "简体中文"},
        {LangName.TChinese , "繁体中文"}
    };
}