using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

try
{
	Console.WriteLine("MCP Client started.");
	  
	// Client metadata
	var clientOptions = new McpClientOptions
	{
	    ClientInfo = new() { Name = "mcp-demo-client", Version = "1.0.0" }
	};
	
	// Create a transport that runs the server project via dotnet
	var transport = new StdioClientTransport(new StdioClientTransportOptions
	{
	    Name = "Demo Server",
	    // Launch the server using dotnet; this avoids the need to hard‑code a path to the executable
	    Command = "dotnet",
	    Arguments = ["run", "--project", "../MCPServer/MCPServer.csproj"]
	});
	
    using var loggerFactory = LoggerFactory.Create(builder =>
        builder.AddConsole().SetMinimumLevel(LogLevel.Information));
  
        // Create the MCP client (this starts the server process)
    await using var mcpClient =
        await McpClientFactory.CreateAsync(transport, clientOptions, loggerFactory: loggerFactory);
  
    // Connect to the local Ollama server and specify a model
    IChatClient ollamaClient = new OllamaChatClient(
        new Uri("http://localhost:11434/"),
        "llama3.2:3b"
    );

    // Build a chat client with function invocation
    IChatClient chatClient = new ChatClientBuilder(ollamaClient)
        .UseFunctionInvocation() // enables tool calls
        .UseLogging(loggerFactory)
        .Build();

    // Discover tools
    var tools = await mcpClient.ListToolsAsync();

    // Chat loop
    var history = new List<ChatMessage>();
    Console.WriteLine("Ask a question (type 'exit' to quit):");

    while (true)
    {
        Console.Write("\nYou: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) continue;
        if (input.Trim().ToLower() == "exit") break;
        history.Add(new ChatMessage(ChatRole.User, input));
        var options = new ChatOptions { Tools = [.. tools] };
        var response = await chatClient.GetResponseAsync(history, options);
        var assistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (assistant != null)
        {
            Console.WriteLine("\nAI: " + string.Join(" ", assistant.Contents.Select(c => c.ToString())));
            history.Add(assistant);
        }
        else
        {
            Console.WriteLine("\nAI: (no response)");
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
}