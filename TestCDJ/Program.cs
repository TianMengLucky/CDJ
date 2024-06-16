using System.Net.Sockets;
using System.Text;

Console.WriteLine("Test CDJ");
Create:
var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

Console.WriteLine("IP:");
var ip = Console.ReadLine();
Console.WriteLine("Port:");
var port = int.Parse(Console.ReadLine()!);
await socket.ConnectAsync(ip!, port);


send:
Console.WriteLine("Code:");
var Code = Console.ReadLine();
Console.WriteLine("Version:");
var Version = Console.ReadLine();
Console.WriteLine("PlayerCount:");
var PlayerCount = Console.ReadLine();
Console.WriteLine("Language:");
var Language = Console.ReadLine();
Console.WriteLine("ServerName:");
var ServerName = Console.ReadLine();
Console.WriteLine("HostName:");
var HostName = Console.ReadLine();
Console.WriteLine("BuildName:");
var BuildName = Console.ReadLine();

var stings = $"{Code}|{Version}|{PlayerCount}|{Language}|{ServerName}|{HostName}";
if (BuildName != null)
{
    stings += $"|{BuildName}";
}

await socket.SendAsync(Encoding.Default.GetBytes(stings));
socket.Dispose();

Console.WriteLine("Parse Option\n 1.Send 2.Create");
var option = int.Parse(Console.ReadLine()!);
if (option == 1)
{
    goto send;
}

if (option == 2)
{
    goto Create;
}

Console.ReadKey();
