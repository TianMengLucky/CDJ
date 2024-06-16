namespace CDJ.CDJData;

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