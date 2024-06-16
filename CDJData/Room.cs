namespace CDJ.CDJData;

public record Room(
    string Code,
    Version? Version,
    int Count,
    LangName LangId,
    string ServerName,
    string PlayerName,
    string BuildId = "")
{
    public DateTime Time { get; set; }
    
    public bool SendEnd { get; set; }
};