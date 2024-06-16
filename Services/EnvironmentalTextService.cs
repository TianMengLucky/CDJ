namespace CDJ.Services;

public class EnvironmentalTextService
{
    public Dictionary<string, string> Texts = new();

    public EnvironmentalTextService Update(string org, string newSting)
    {
        Texts[org] = newSting;
        return this;
    }

    public string Replace(string org)
    {
        foreach (var (text, newText) in Texts)
        {
            org = org.Replace('{' + text + '}', newText);
        }

        return org;
    }
}