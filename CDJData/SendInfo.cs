namespace CDJ.CDJData;

public class SendInfo(string message)
{
    public string Message = message;
    public List<(bool, long)> SendTargets = [];
}