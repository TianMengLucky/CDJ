namespace CDJ.CDJData;

public abstract class SendMessage
{
    public abstract string message_type { get; }
    
    public string message { get; set; }
}