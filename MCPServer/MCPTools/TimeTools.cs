using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPServer.MCPTools;

[McpServerToolType]
public static class TimeTools
{
    [McpServerTool, Description("Returns the current UTC time.")]
    public static DateTime GetUtcNow() => DateTime.UtcNow;
}