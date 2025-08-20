using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPServer.MCPTools;

[McpServerToolType]
public static class EchoTools
{
    [McpServerTool, Description("Echoes your message back.")]
    public static string Echo(string message) => message;

    [McpServerTool, Description("Reverses the string you provide.")]
    public static string ReverseEcho(string message) => new string(message.Reverse().ToArray());
}