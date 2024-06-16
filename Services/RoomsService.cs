using System.Diagnostics.CodeAnalysis;
using CDJ.CDJData;
using CDJ.Config;
using Microsoft.Extensions.Options;

namespace CDJ.Services;

public class RoomsService(EnvironmentalTextService service, IOptions<ServerConfig> config)
{
    public readonly List<Room> _Rooms = [];
    
    public bool TryPareRoom(string text,[MaybeNullWhen(false)] out Room room)
    {
        room = null;
        try
        {
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

            service
                .Update("RoomCode", code)
                .Update("Version", version?.ToString() ?? BuildVersion)
                .Update("PlayerCount", count.ToString())
                .Update("Language", config.Value.LangTextToCN ? lang[langId] : Enum.GetName(langId) ?? "UnKnown Lang")
                .Update("ServerName", serverName)
                .Update("PlayerName", playName);
            
            room = new Room(code, version, count, langId, serverName, playName, BuildVersion);
            _Rooms.Add(room);
        }
        catch (Exception e)
        {
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