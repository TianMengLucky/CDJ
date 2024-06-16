namespace CDJ.CDJData;

public class UserMessage : SendMessage
{
    public override string message_type { get; } = "private";
    public string user_id { get; set; }
}