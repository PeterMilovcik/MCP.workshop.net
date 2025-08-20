# MCP Workshop Guide – Building Servers and Clients in .NET 8

This comprehensive guide merges the theory of the Model Context Protocol (MCP) with practical tutorials for implementing MCP servers and clients in C#. 
The material is divided into chapters so you can follow it sequentially or jump directly to the parts you need.

## Chapter 1 – Understanding the Model Context Protocol

### 1.1 What is MCP?

The **Model Context Protocol (MCP)** is an open standard for connecting AI models to external information, tools or resources. In an MCP system there are two roles:

- **MCP server** – exposes functionality through **resources**, **tools** and **prompts** [1](https://modelcontextprotocol.io/quickstart/server#:~:text=Core%20MCP%20Concepts). Tools are functions the model can call, resources provide data and prompts supply additional context or instructions.
- **MCP client** – typically a language‑model agent that calls server tools via JSON‑RPC. The client sends tool requests to the server and passes the JSON responses back to the model. The model decides when to call a tool during a conversation.

Because models are stateless, the client re‑sends relevant messages and tool results with each request. This pattern allows the AI to “remember” previous interactions without persisting state on the server.

### 1.2 Resources, tools and prompts

The MCP specification categorizes server capabilities into three groups [1](https://modelcontextprotocol.io/quickstart/server#:~:text=Core%20MCP%20Concepts):

| Capability    | Purpose                                                                                                           |
| ------------- | ----------------------------------------------------------------------------------------------------------------- |
| **Resources** | Expose data sources such as files, databases or APIs; the model can read or search them.                          |
| **Tools**     | Functions that perform actions (calculations, API calls, file operations). Most examples in this guide use tools. |
| **Prompts**   | Predefined strings that provide context or instructions to the model.                                             |

In many simple integrations, only tools are needed. Resources are useful when you want to provide large datasets to the model, and prompts can guide the model’s behavior.

### 1.3 Server responsibilities

An MCP server must:

- Register tools so clients can discover them. In the .NET SDK, you mark classes with `[McpServerToolType]` and methods with `[McpServerTool]` to indicate they are tools [2](https://medium.com/@mutluozkurt/creating-an-mcp-server-and-client-with-net-a-step-by-step-guide-0c3833dde3c4#:~:text=Now%2C%20update%20your%20,as%20follows).
- Dispatch JSON‑RPC calls to the appropriate tool method and return results or errors. The SDK handles the messaging and dispatch for you.
- Log messages to stderr if you use the standard input/output (STDIO) transport, to avoid corrupting the JSON‑RPC stream [1](https://modelcontextprotocol.io/quickstart/server#:~:text=Core%20MCP%20Concepts).

### 1.4 Client responsibilities

The client acts as a bridge between the language model and the server:

1. **Connect to the server.** You provide a transport object (e.g. `StdioClientTransport` or an HTTP transport) that knows how to launch or reach the server. The client uses `McpClientFactory.CreateAsync` to establish the connection.
2. **Discover tools.** After connecting, call `ListToolsAsync()` to get a list of available tools [3](https://markheath.net/post/2025/4/14/calling-mcp-server-microsoft-extensions-ai#:~:text=The%20next%20step%20is%20simply,capabilities%20of%20an%20MCP%20server). Each tool is represented as an `McpClientTool` (derived from `AIFunction`) that can be passed to your AI model.
3. **Integrate with an AI model.** The client maintains a chat history. For each user message, you send the history and the tool list to the model. The model may return a function call, which the client then invokes on the server. Use `ChatClientBuilder` and `.UseFunctionInvocation()` from the AI extensions library to automate this pattern [4](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI#:~:text=using%20Microsoft).
4. **Maintain context.** Because the language model is stateless, always include the conversation history and any tool outputs in each request.

### 1.5 Communication and transport

MCP uses JSON‑RPC 2.0 for communication. A request includes a method name (the tool to call), parameters and an id. The server replies with either a result or an error. Transports determine how messages are carried:

- **STDIO:** The server and client communicate over standard input and output streams. This is convenient for local development or integration with desktop applications. Use `StdioClientTransport` on the client and `WithStdioServerTransport` on the server.
- **HTTP:** The server exposes an HTTP endpoint; the client sends POST requests. This is useful for remote or cloud deployments.
- **Custom transports:** MCP allows custom transports if you need WebSockets or other protocols.

The rest of this guide builds on this foundation with concrete implementations.

## Chapter 2 – Building an MCP Server and Client with the Official SDK

This chapter walks you through creating an MCP server and client in C# using the **`ModelContextProtocol`** library. The example uses the STDIO transport so the client can launch the server process locally. All NuGet packages are pinned to a consistent set of versions that have been tested to work together.

### 2.1 Prerequisites

- **.NET 8 SDK** installed (or higher)
- NuGet package versions:
	- `ModelContextProtocol` **0.1.0-preview.8** (server and client library).
	- `Microsoft.Extensions.AI` **9.7.0** (AI abstractions and chat client builders).
	- `Microsoft.Extensions.AI.Ollama` **9.7.0-preview.1.25356.2** if you plan to use a local Ollama model.
	- `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Console` **9.0.8** (for logging).

### 2.2 Project setup

Create a solution with two console applications—one for the server and one for the client:

```
dotnet new sln --name McpWorkshop  
```

```
dotnet new console -n MCPServer  
```

```
dotnet new console -n MCPClient  
```

```
dotnet sln McpWorkshop.sln add MCPServer/MCPServer.csproj MCPClient/MCPClient.csproj
```

Install the required packages for the server:

```
cd MCPServer
```

```
dotnet add package ModelContextProtocol --version 0.1.0-preview.8
```

```
dotnet add package Microsoft.Extensions.Hosting
```

```
dotnet add package Microsoft.Extensions.Logging --version 9.0.8
```

```
dotnet add package Microsoft.Extensions.Logging.Console --version 9.0.8
```

Install the packages for the client:

```
cd ../MCPClient
```

```
dotnet add package ModelContextProtocol --version 0.1.0-preview.8
```

```
dotnet add package Microsoft.Extensions.AI --version 9.7.0
```

```
dotnet add package Microsoft.Extensions.Logging --version 9.0.8
```

```
dotnet add package Microsoft.Extensions.Logging.Console --version 9.0.8
```

If you intend to use local models via Ollama, also install `Microsoft.Extensions.AI.Ollama` **9.7.0-preview.1.25356.2**. For OpenAI integration, install `Microsoft.Extensions.AI.OpenAI` 9.7.x as shown in Chapter 3.

```
dotnet add package Microsoft.Extensions.AI.Ollama --version 9.7.0-preview.1.25356.2
```

### 2.3 Implementing the server

```
cd ..
```

```
code .
```

Replace `MCPServer/Program.cs` with the following code. It uses the generic host to register an MCP server that communicates via STDIO and automatically discovers tools in the current assembly [5](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/#:~:text=Let%E2%80%99s%20update%20our%20,from%20the%20running%20assembly).

``` csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to write to stderr (important for STDIO transport)
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

// Register the MCP server and use STDIO as the transport
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

The call to `.WithToolsFromAssembly()` scans the assembly for methods decorated with `[McpServerToolType]` and `[McpServerTool]` [2](https://medium.com/@mutluozkurt/creating-an-mcp-server-and-client-with-net-a-step-by-step-guide-0c3833dde3c4#:~:text=Now%2C%20update%20your%20,as%20follows). 
Tools defined in separate files are automatically registered.

#### 2.3.1 Defining tools

Create a folder `MCPTools` in the server project and add your tool classes. Each class should be static and annotated with `[McpServerToolType]`, and each method should be annotated with `[McpServerTool]` and, optionally, `DescriptionAttribute`:

File: `MCPTools/EchoTools.cs`  

``` csharp
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
```
  
File: `MCPTools/TimeTools.cs`

``` csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPServer.MCPTools;

[McpServerToolType]
public static class TimeTools
{
    [McpServerTool, Description("Returns the current UTC time.")]
    public static DateTime GetUtcNow() => DateTime.UtcNow;
}
```

#### 2.3.2 Testing with MCP Inspector (optional)

The **MCP Inspector** lets you explore and invoke your tools from a web interface. Install it via npm, run your server in one terminal, then run `npx @modelcontextprotocol/inspector dotnet run` in another. Open the provided URL to list and call the tools.

```
npm install -g @modelcontextprotocol/inspector
```

```
cd MCPServer
```

```
npx @modelcontextprotocol/inspector dotnet run
```

- Browser opens with `http://localhost:6274/`
- Click on `Connect`
- Click on `List Tools`
- Select tools and test them

#### 2.3.3 Integrating the server with VS Code Agent Mode

Once you have verified that your server works with MCP Inspector, you can add it to Visual Studio Code and use its tools directly in the **agent mode** chat experience. Agent mode runs a large‑language model with access to MCP tools; the steps below show how to connect your local server.

**Step 1 – Build your server.** Run `dotnet build` in the `MCPServer` project to ensure the server can start. VS Code will invoke this command when launching the server via STDIO.

**Step 2 – Add the server to VS Code.** VS Code discovers servers through an `mcp.json` file or via the **MCP: Add Server** command. To make the configuration part of your workspace, create a `.vscode` directory in your solution and add `mcp.json` like this:

``` json
{
  "servers": { 
    "demoServer": { 
      "type": "stdio",  
      "command": "dotnet",  
      "args": ["run", "--project", "${workspaceFolder}/MCPServer/MCPServer.csproj"]  
    }  
  }  
}
```

This configuration tells VS Code to run your server using the dotnet run command when needed. The `${workspaceFolder}` variable resolves to the root of your workspace, making the path portable across machines. When you open a project containing this file, VS Code prompts you to confirm that you trust the server before starting it [6](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=Caution), then discovers the server’s tools and caches them for subsequent sessions [7](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=When%20VS%20Code%20starts%20the,command%20in%20the%20Command%20Palette). Alternatively, press `Ctrl+Shift+P` (or `⌘+Shift+P` on macOS) and run **MCP: Add Server**. Choose **`stdio`** as the transport, provide a name (for example `demoServer`), set the command to dotnet and the arguments to run, `--project`, and the path to your server project. VS Code writes the configuration for you [8](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=3,if%20it%20doesn%27t%20already%20exist).

**Step 3 – Use tools in agent mode.** Open the **Chat** view and select **Agent mode** from the drop‑down at the top of the chat pane [9](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=Use%20MCP%20tools%20in%20agent,mode). Click the **Tools** button to show the list of available tools and select those you want to enable [10](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=Use%20MCP%20tools%20in%20agent,mode). A chat can enable up to 128 tools at once [11](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=Important). Type a prompt such as `“Reverse the string ‘hello world’”`. When the model decides to call a tool, VS Code asks you to confirm the invocation; you can approve once, for the session, or for all future invocations [12](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=By%20default%2C%20when%20a%20tool,that%20modify%20files%20or%20data). You can also reference a tool directly by typing `#` followed by its name in your prompt [13](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=You%20can%20also%20directly%20reference,ask%2C%20edit%2C%20and%20agent%20mode). If the tool has input parameters, VS Code presents a form so you can review or edit the values before running [14](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=4,parameters%20before%20running%20the%20tool). After the tool executes, its result appears in the chat and becomes part of the context.

**Step 4 – Manage your server.** Use the **MCP: Show Installed Servers** command or the MCP Servers section of the Extensions view to start, stop or restart your server, view logs, or clear its cached tools [15](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=To%20view%20and%20manage%20the,section%20in%20the%20Extensions%20view). If you change your server code or add new tools, run **MCP: Reset Cached Tools** so VS Code reloads the server’s capabilities [7](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=When%20VS%20Code%20starts%20the,command%20in%20the%20Command%20Palette). Remember that MCP servers can execute arbitrary code; only add servers from trusted sources and review their configurations before running [6](https://code.visualstudio.com/docs/copilot/chat/mcp-servers#:~:text=Caution).
### 2.4 Implementing the client with STDIO transport

We use `StdioClientTransport` to launch the server process and communicate over its `stdin/stdout` streams. 

Here is the core of `MCPClient/Program.cs`:

``` csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

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
  
try
{
    using var loggerFactory = LoggerFactory.Create(builder =>
        builder.AddConsole().SetMinimumLevel(LogLevel.Information));
  
    // Create the MCP client (this starts the server process)
    await using var mcpClient =
        await McpClientFactory.CreateAsync(transport, clientOptions, loggerFactory: loggerFactory);
  
    // Discover tools
    var tools = await mcpClient.ListToolsAsync();
  
    // TODO: integrate with an AI model – see Chapters 3 and 4
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
}
```

At this point the client has launched the server and listed its tools (see console output). 
The next steps depend on the model you want to use, which we cover in the following chapters.

## Chapter 3 – Integrating with OpenAI

This chapter shows how to connect your MCP server to OpenAI’s models using the `Microsoft.Extensions.AI.OpenAI` package. It assumes you have completed Chapter 2 and already have a server and client project.

### 3.1 Prerequisites

- An **OpenAI API key** stored in an environment variable named `OPENAI_API_KEY`.
- Install the OpenAI client library in your **client** project:

```
dotnet add package Microsoft.Extensions.AI.OpenAI --version 9.7.1-preview.1.25365.4
```

### 3.2 Client implementation for OpenAI

Update `MCPClient/Program.cs` to replace the placeholder AI client with an OpenAI client and to wire up function invocation:

``` csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

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
try
{
    using var loggerFactory = LoggerFactory.Create(builder =>
        builder.AddConsole().SetMinimumLevel(LogLevel.Information));
    // Create the MCP client (this starts the server process)
    await using var mcpClient =
        await McpClientFactory.CreateAsync(transport, clientOptions, loggerFactory: loggerFactory);
    // Read API key
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY not set");
    // Create the OpenAI chat client.  Use a model like gpt-4o or gpt-3.5-turbo.
    IChatClient openAiChatClient = new OpenAI.Chat.ChatClient("gpt-4o", apiKey).AsIChatClient();
    // Build a higher‑level client with function invocation
    IChatClient chatClient = new ChatClientBuilder(openAiChatClient)
        .UseFunctionInvocation()  // enables tool calls
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
```

You can now ask questions like “Reverse the string "hello world", "What time is it?", or echo some message. The model may call the `ReverseEcho`, `GetUtcNow` , or `Echo` tools you defined earlier. If the model doesn’t choose to call a tool, try rephrasing your prompt (e.g. “Use the reverse tool on ‘hello world’”).

```
Reverse the string "hello world"
```

```
What time is it?
```

```
Echo following message: "You are awesome!"
```

### 3.3 Notes on OpenAI integration

- Ensure all `Microsoft.Extensions.AI.*` packages (including `OpenAI`) are on the same version (in this case 9.7.0). Mismatched versions can cause runtime errors. The working package list is:

``` xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.7.0" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.7.0-preview.1.25356.2" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.8" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.8" />
<PackageReference Include="ModelContextProtocol" Version="0.1.0-preview.8" />
```

- Manage API usage to avoid unexpected costs; each call to the OpenAI API counts against your token allowance.
- Experiment with different models (`gpt-3.5-turbo`, `gpt-4o`) and parameters (`temperature`, `top‑p`) to control how often the model calls functions.

## Chapter 4 – Using a Local Model with Ollama

If you prefer to avoid external API calls, you can run large‑language models locally with **Ollama**. This chapter summarizes installation and integration steps and clarifies Windows‑specific behavior.

### 4.1 Installing Ollama

#### Linux

Run the official installer script:

```
curl -fsSL https://ollama.com/install.sh | sh
```

This downloads and installs the runtime [6](https://raw.githubusercontent.com/ollama/ollama/main/docs/linux.md#:~:text=To%20install%20Ollama%2C%20run%20the,following%20command). Alternatively, download the `tarball` and extract it into `/usr`:

```
curl -LO https://ollama.com/download/ollama-linux-amd64.tgz  
sudo tar -C /usr -xzf ollama-linux-amd64.tgz  
ollama serve &  # start the server  
ollama -v       # verify installation[7]
```

#### macOS

Download the `ollama.dmg` from the official site, mount it and drag the app to your **Applications** folder. On first launch, the app ensures that the ollama CLI is in your PATH [8](https://raw.githubusercontent.com/ollama/ollama/main/docs/macos.md#:~:text=The%20preferred%20method%20of%20installation,usr%2Flocal%2Fbin). You can change the install location by moving the app and linking Ollama.app/Contents/Resources/ollama into your path [9](https://raw.githubusercontent.com/ollama/ollama/main/docs/macos.md#:~:text=).

#### Windows

Ollama for Windows installs as a native application with GPU support [10](https://raw.githubusercontent.com/ollama/ollama/main/docs/windows.md#:~:text=Ollama%20now%20runs%20as%20a,run%20in%20the%20background%20and). Download the .exe installer from the official website and double‑click it. Follow the wizard; no administrator privileges are required [11](https://dev.to/evolvedev/how-to-install-ollama-on-windows-1ei5#:~:text=Download%3A%20Visit%20the%20Ollama%20Windows,download%20an%20executable%20installer%20file). 

**Important:** the installer registers Ollama as a Windows Service, which starts automatically in the background. You do **not** need to run `ollama serve` manually. Killing the `ollama.exe` process will cause the service controller to restart it.

### 4.2 Pulling and running models

After installation, pull a model. For example, the Llama 3.2 model:

```
ollama pull llama3.2:3b
```

To run a model interactively:

```
ollama run llama3.2
```

To serve models over an HTTP API (Linux/macOS or Windows service already running; only on Linux/macOS where the service isn’t auto‑started):

```
ollama serve
```

The API is available at http://localhost:11434 [12](https://raw.githubusercontent.com/ollama/ollama/main/docs/windows.md#:~:text=After%20installing%20Ollama%20for%20Windows%2C,http%3A%2F%2Flocalhost%3A11434). On Windows this service starts automatically; there is no need to run serve yourself, and you can confirm the port is in use with `netstat`. If you see a PID belonging to ollama.exe, the service is running.

```
netstat -ano | findstr 11434
```

### 4.3 Integrating Ollama into your MCP client

Use the `Microsoft.Extensions.AI.Ollama` package (version `9.7.0-preview.1.25356.2`) to connect to your local server:

```
dotnet add package Microsoft.Extensions.AI.Ollama --version 9.7.0-preview.1.25356.2
```

``` csharp
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
```

Make sure the Ollama server is running (or on Windows, the service is active). You can then run your MCP client and ask questions; the local model will decide when to call your server’s tools.

### 4.4 Best practices

- **Do not run** **`ollama serve`** **on Windows**; the service runs automatically. Use `netstat` or PowerShell’s `Get-NetTCPConnection` to verify that port `11434` is bound to `ollama.exe`.
- Models require significant disk space (this model is about 1.88 GB). 
- On Windows they are stored in `%HOMEPATH%\.ollama` [13](https://raw.githubusercontent.com/ollama/ollama/main/docs/windows.md#:~:text=); on Linux and macOS they reside in `~/.ollama`.
- Keep your GPU drivers up to date, especially on Windows where Ollama uses hardware acceleration [11](https://dev.to/evolvedev/how-to-install-ollama-on-windows-1ei5#:~:text=Download%3A%20Visit%20the%20Ollama%20Windows,download%20an%20executable%20installer%20file).
- Use `.UseFunctionInvocation()` when building the chat client so your model can call tools automatically [4](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI#:~:text=using%20Microsoft).

## Chapter 5 – Troubleshooting

### 5.1 Port conflicts

If you see an error such as:

```
Error: listen tcp 127.0.0.1:11434: bind: Only one usage of each socket address (protocol/network address/port) is normally permitted.
```

it means another process (usually another instance of Ollama) is already using port 11434. On Windows this is expected because the Ollama service runs automatically. Use `netstat -ano` to confirm the PID and `tasklist /FI "PID eq <pid>"` to see that it is `ollama.exe`. You do not need to kill or restart it; simply connect to `http://localhost:11434`.

### 5.2 Missing methods and version mismatches

Exceptions like `System.MissingMethodException: Method not found: 'System.String Microsoft.Extensions.AI.ChatResponse.get_ChatThreadId()` indicate that different `Microsoft.Extensions.AI` packages are on incompatible versions. Resolve this by aligning all AI packages to the same version (e.g. `9.7.0`) and re‑building your project. Avoid mixing preview and non‑preview versions unless they share the same version number.

## 6 – Adding an Azure DevOps Test‑Case Tool

The previous chapters showed how to build an MCP server, test it in the MCP Inspector, integrate it with VS Code’s agent mode, and build clients for local and cloud LLMs. We’ll now extend the server with a **new tool** that queries Azure DevOps to retrieve test‑case results from the latest successful build of a pipeline.

### 6.1 Overview and prerequisites

This tool lets your AI assistant answer questions like “What was the outcome of the `LoginTests` test case in project A’s pipeline?” by calling Azure DevOps. To do this we must:

1. **Install extra NuGet packages** in the server project.
2. **Provide the server with environment variables** for the Azure DevOps collection URL and PAT.
3. **Define a data type** (`TestCaseResult`) for returned results.
4. **Implement a tool method** that accepts the project name, repository, pipeline (definition) name, an optional branch (default `main`), and a test case title substring; fetches the latest successful build on that branch; scans its test runs; filters results by title; and returns the outcome and duration.
5. **Prompt the user for missing parameters** when required.

#### 6.1.1 Install required NuGet packages

Run these commands in your **MCPServer** directory to add Azure DevOps client libraries:

```
cd ../MCPServer
```

```bash
dotnet add package Microsoft.TeamFoundationServer.Client --version 19.225.1
```

These packages provide `VssConnection`, `BuildHttpClient`, and `TestManagementHttpClient`.

#### 6.1.2 Set environment variables

Before running the server, set:

- `AZURE_DEVOPS_COLLECTION_URL` — your organization’s collection URL (e.g. `https://dev.azure.com/my‑org`).
- `AZURE_DEVOPS_PAT` — a Personal Access Token with **Build (Read)** and **Test Management (Read)** scopes.

Do not commit these secrets to source control. For example, in PowerShell:

```powershell
$env:AZURE_DEVOPS_COLLECTION_URL = "https://dev.azure.com/my-org"
$env:AZURE_DEVOPS_PAT = "your-token-here"
```

On Linux/macOS:

```bash
export AZURE_DEVOPS_COLLECTION_URL="https://dev.azure.com/my-org"
export AZURE_DEVOPS_PAT="your-token-here"
```

#### 6.1.3 Define a new tool class

Add a new file `AzureDevOpsTools.cs` inside the `MCPTools` folder of your server. Mark the class with `[McpServerToolType]` and the method with `[McpServerTool]` so the MCP host automatically registers it. The method uses Azure DevOps client APIs to locate the build and test results, and returns a `List<TestCaseResult>`.

File: `MCPTools/AzureDevOpsTools.cs`

```csharp
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using ModelContextProtocol.Server;

namespace MCPServer.MCPTools;

public record TestCaseResult(string Title, string Outcome, double DurationMs, string? ErrorMessage = null, string? StackTrace = null);

public record GetTestCaseResultsResponse(
    bool Success,
    string LogMessages,
    List<TestCaseResult> TestResults,
    string? ErrorMessage = null);

[McpServerToolType]
public class AzureDevOpsTools
{
    /// <summary>
    /// Get test case results from the latest successful/partially successful build of a pipeline/definition.
    /// </summary>
    [McpServerTool, Description("Retrieve test case results from the latest successful build of a pipeline/definition in Azure DevOps.")]
    public static async Task<GetTestCaseResultsResponse> GetTestCaseResultsAsync(
        string projectName,
        string definitionName,
        string testCaseTitle)
    {
        var logMessages = new List<string>();
        var testResults = new List<TestCaseResult>();
        
        try
        {
            logMessages.Add($"Starting GetTestCaseResults for project: {projectName}, definition: {definitionName}, testCase: {testCaseTitle}");
            
            // Validate inputs and elicit missing parameters
            if (string.IsNullOrWhiteSpace(projectName))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Project name is required");
            if (string.IsNullOrWhiteSpace(definitionName))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Definition (pipeline) name is required");
            if (string.IsNullOrWhiteSpace(testCaseTitle))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Test case title is required");
            
            // Read environment variables
            string? collectionUrl = Environment.GetEnvironmentVariable("AZURE_DEVOPS_COLLECTION_URL");
            string? pat = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
            if (string.IsNullOrWhiteSpace(collectionUrl) || string.IsNullOrWhiteSpace(pat))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "AZURE_DEVOPS_COLLECTION_URL and AZURE_DEVOPS_PAT must be set");
            
            logMessages.Add($"Using Azure DevOps collection URL: {collectionUrl}");
            
            // Connect using PAT
            var creds = new VssBasicCredential(string.Empty, pat);
            var connection = new VssConnection(new Uri(collectionUrl), creds);
            
            logMessages.Add("Connecting to Azure DevOps...");
            var buildClient = await connection.GetClientAsync<BuildHttpClient>();
            var testClient = await connection.GetClientAsync<TestManagementHttpClient>();
            logMessages.Add("Successfully connected to Azure DevOps");
            
            // Find build definition by name
            logMessages.Add($"Looking for build definition: {definitionName}");
            var definitions = await buildClient.GetDefinitionsAsync(project: projectName, name: definitionName);
            var definition = definitions.FirstOrDefault();
            if (definition == null)
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, $"Build definition '{definitionName}' not found in project '{projectName}'.");
            
            logMessages.Add($"Found build definition with ID: {definition.Id}");
            
            logMessages.Add($"Getting latest successful or partially successful build...");
            var builds = await buildClient.GetBuildsAsync(
                project: projectName,
                definitions: [definition.Id],
                resultFilter: BuildResult.Succeeded | BuildResult.PartiallySucceeded,
                statusFilter: BuildStatus.Completed,
                branchName: null,
                top: 1);
            
            var build = builds.FirstOrDefault();
            if (build == null)
            {
                // If no build found with the specified branch, let's try without branch filter to see what branches exist
                logMessages.Add($"No builds found.");
                
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                    $"No completed successful or partially successful build found for definition '{definitionName}'.");
            }
            
            logMessages.Add($"Found build ID: {build.Id}, Build Number: {build.BuildNumber}");
            
            logMessages.Add("Getting test runs for build...");
            var testRuns = await testClient.GetTestRunsAsync(projectName, buildUri: build.Uri.ToString());
            logMessages.Add($"Found {testRuns.Count} test runs");
            
            foreach (var run in testRuns)
            {
                var runResults = await testClient.GetTestResultsAsync(projectName, run.Id);
                foreach (var r in runResults)
                {
                    if (!string.IsNullOrWhiteSpace(r.TestCaseTitle) &&
                        r.TestCaseTitle.Contains(testCaseTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        logMessages.Add($"Found matching test case: {r.TestCaseTitle}, Outcome: {r.Outcome}");
						testResults.Add(new TestCaseResult(
                            r.TestCaseTitle!,
                            r.Outcome,
                            r.DurationInMs,
                            r.ErrorMessage,
                            r.StackTrace));
                    }
                }
            }
            
            if (testResults.Count == 0)
            {
                logMessages.Add($"No test case results matching '{testCaseTitle}' were found");
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                    $"No test case results matching '{testCaseTitle}' were found.");
            }
            
            logMessages.Add($"Successfully found {testResults.Count} matching test case results");
            return new GetTestCaseResultsResponse(true, string.Join("\n", logMessages), testResults);
        }
        catch (Exception ex)
        {
            logMessages.Add($"ERROR: {ex.GetType().Name}: {ex.Message}");
            logMessages.Add($"Stack trace: {ex.StackTrace}");
            return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                $"Exception occurred: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
```

**Notes:**

- The method reads the collection URL and PAT from environment variables. If they are missing, it throws an exception so the assistant can prompt the user to set them.
- It retrieves the build definition ID from its name, then calls `GetBuildsAsync` to find the latest successful build on the given branch.
- It uses `GetTestRunsAsync` and `GetTestResultsAsync` to fetch test runs and results. Each result exposes `TestCaseTitle`, `Outcome` and `DurationInMs` fields.
- Missing arguments are checked explicitly with `ArgumentException` so the MCP runtime can ask the user for the missing value.

#### 6.1.4 Testing the tool

1. **Run the server** with the environment variables set.
2. **Test in MCP Inspector** (optional): Connect to your server and call `GetTestCaseResultsAsync` with real project, pipeline and test-case names; the inspector shows the JSON result.
3. **Test in Visual Studio Code (Agent Mode)**:
	- Ensure your server is registered in `.vscode/mcp.json` or added via _MCP: Add Server_ (see Section 2.3.3). Use the built `.exe` path and `stdio` transport.
	- In the Agent chat, ask a question like:  
		- _“Find the result of the test case **LoginTests** in the `WebApp‑CI` pipeline for project **MyProject**.”_  
		- The LLM should decide to call `GetTestCaseResultsAsync`. You’ll be asked to approve the call, then the results appear.
	- If the assistant reports that environment variables are missing, set `AZURE_DEVOPS_COLLECTION_URL` and `AZURE_DEVOPS_PAT` and restart the server.

This concludes the advanced extension of your MCP server. It demonstrates how to securely access external services (like Azure DevOps) and return structured data to your LLM assistant. You can apply the same pattern to build tools for other DevOps operations (e.g. work‑item queries, build creation).

## 7 – Deploying Your MCP Server with Docker

Containerizing your server makes it trivial to run anywhere Docker is installed, without worrying about .NET versions or host dependencies. This chapter shows how to remove the example tools, add container support to your project, build a local image, and run it (including in VS Code Agent Mode). The server is **not published to any public registry**—it remains local.

### 7.1 Remove sample tools from the server

In the `MCPServer` project, delete or exclude any classes you don’t intend to ship (e.g. `EchoTools.cs`, `TimeTools.cs`). Keep only `AzureDevOpsTools.cs` so `WithToolsFromAssembly()` registers just your Azure DevOps tool.

### 7.2 Enable built‑in container support in the project file

.NET 8 can automatically build a Docker image when you publish. Add a `<PropertyGroup>` to `MCPServer.csproj` as shown below:

```xml
<PropertyGroup>
  <!-- Enable SDK container support -->
  <EnableSdkContainerSupport>true</EnableSdkContainerSupport>
  <!-- Name of the resulting image (local only) -->
  <ContainerRepository>azuredevops/mcpserver</ContainerRepository>
  <!-- Base image used for the final runtime layer (alpine keeps it small) -->
  <ContainerBaseImage>mcr.microsoft.com/dotnet/runtime:8.0-alpine</ContainerBaseImage>
  <!-- Target a Linux runtime for maximum portability -->
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
</PropertyGroup>
```

Because your server uses STDIO and doesn’t expose network ports, no `ContainerPort` is needed.

### 7.3 Build the Docker image

From the `MCPServer` project root, run:

```bash
dotnet publish /t:PublishContainer -c Release
```

The `/t:PublishContainer` target builds your project, publishes it, and creates a Docker image tagged with the name specified in `ContainerRepository`. After it finishes, confirm with:

```bash
docker images
# REPOSITORY                  TAG       IMAGE ID       CREATED        SIZE
# azuredevops/mcpserver       latest    <image-id>     <seconds-ago>  <~100MB>
```

This image exists only on your local machine; you haven’t pushed it to a registry.

### 7.4 Running the container locally

Run your container and pass the Azure DevOps settings as environment variables. For example:

```bash
docker run -i --rm \
  -e AZURE_DEVOPS_COLLECTION_URL=https://dev.azure.com/my-org \
  -e AZURE_DEVOPS_PAT=YOUR_PAT_TOKEN \
  azuredevops/mcpserver
```

- `-i` keeps STDIN open (required for MCP’s stdio transport).
- `--rm` removes the container after it stops.
- Use `--env-file` instead if you prefer to load variables from a file.
 
When the container runs, it starts your MCP server and waits for requests over STDIO.

### 7.5 (Alternative) Multi‑stage Dockerfile

If you prefer to manage the Dockerfile yourself (e.g. to customize build steps or support older SDK versions), create a `Dockerfile` at your solution root:

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MCPServer/MCPServer.csproj", "MCPServer/"]
RUN dotnet restore "MCPServer/MCPServer.csproj"
COPY . .
WORKDIR "/src/MCPServer"
RUN dotnet publish "MCPServer.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MCPServer.dll"]
```

Then build and run:

```bash
docker build -t azuredevops/mcpserver .
docker run -i --rm -e AZURE_DEVOPS_COLLECTION_URL=... -e AZURE_DEVOPS_PAT=... azuredevops/mcpserver
```

### 7.6 Use your Dockerized server in VS Code Agent Mode

To invoke the containerized server from VS Code (or any MCP client), point the client’s command to `docker run` instead of `dotnet`. For VS Code, add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "azuredevops-local": {
      "type": "stdio",
      "command": "docker",
      "args": [
        "run",
        "-i",
        "--rm",
        "-e", "AZURE_DEVOPS_COLLECTION_URL=https://dev.azure.com/my-org",
        "-e", "AZURE_DEVOPS_PAT=YOUR_PAT_TOKEN",
        "azuredevops/mcpserver"
      ]
    }
  }
}
```

When you open a Chat > Agent conversation, VS Code will spin up the container automatically on demand and connect via STDIO. You never have to worry about local .NET installations or file paths.

### 7.7 (Option) Running via Docker MCP Toolkit

Docker Desktop’s _MCP Toolkit_ offers a managed gateway that can run your container and connect it to multiple clients. To use it without publishing to a registry:

1. Enable the MCP Toolkit in Docker Desktop (in Settings > Beta features).
2. In a terminal, load your image into Docker Desktop (it’s already local from the previous steps).
3. In Docker Desktop, open _MCP Toolkit_ → _Catalog_ → **Add local server**, and select your `azuredevops/mcpserver` image. Configure the `AZURE_DEVOPS_*` variables in the Config tab if offered.
4. Connect your client (e.g. VS Code) to the MCP Gateway by adding the following to your VS Code `mcp.json`:
   
```json
{
  "servers": {
	"MCP_DOCKER_GATEWAY": {
	  "type": "stdio",
	  "command": "docker",
	  "args": ["mcp", "gateway", "run"]
	}
  }
}
```
    
    Then run `docker mcp client connect vscode` to write `.vscode/mcp.json` automatically.
    

Because your image is not pushed to Docker Hub, it remains private to your machine. If you later want to share it with teammates, push it to a private registry and update the `ContainerRepository` name accordingly.

### 7.8 Summary

By enabling .NET’s built‑in container support or using a multi‑stage Dockerfile, you can package your MCP server—now streamlined to only your `AzureDevOpsTools`—into a lightweight image. Running it with `docker run` (passing the necessary Azure DevOps environment variables) allows any MCP client, including VS Code Agent Mode, to use your tools reliably, without requiring a local .NET runtime.

### 8 – Packaging and Publishing Your MCP Server with NuGet

NuGet now supports hosting **MCP server packages**. Publishing your server as a package allows others to discover and install it through NuGet search. This chapter adapts the official NuGet quickstart to our Azure DevOps tool.

#### 8.1 Prepare the `.mcp/server.json`

The `.mcp/server.json` file defines metadata and inputs for your server. Update it as follows (replace placeholders with your information):

```json
{
  "description": "An MCP server that queries Azure DevOps test results",
  "name": "io.github.yourusername/AzureDevOpsMcpServer",
  "packages": [
    {
      "registry_name": "nuget",
      "name": "YourUsername.AzureDevOpsMcpServer",
      "version": "1.0.0",
      "package_arguments": [],
      "environment_variables": [
        { "name": "AZURE_DEVOPS_COLLECTION_URL", "description": "Base URL of your Azure DevOps organisation", "is_required": true, "is_secret": false },
        { "name": "AZURE_DEVOPS_PAT", "description": "Personal Access Token for Azure DevOps", "is_required": true, "is_secret": true }
      ]
    }
  ],
  "repository": {
    "url": "https://github.com/yourusername/AzureDevOpsMcpServer",
    "source": "github"
  },
  "version_detail": { "version": "1.0.0" }
}
```

The `environment_variables` array declares the variables your tool needs; hosts like VS Code will prompt users for these values.

#### 8.2 Set a Package ID in the project file

To ensure your package has a unique identifier, add `<PackageId>` to the **MCPServer.csproj**:

```xml
<PropertyGroup>
  <PackageId>YourUsername.AzureDevOpsMcpServer</PackageId>
</PropertyGroup>
```

This matches the `name` field in `server.json`.

#### 8.3 Pack the project

Run the `dotnet pack` command to generate a NuGet package. Use the Release configuration so the package includes optimized binaries:

```bash
dotnet pack -c Release
```

This creates a `.nupkg` file in the `bin/Release` folder.

#### 8.4 Publish the package

To share your server privately, push the package to a test feed or internal NuGet server. Avoid publishing to the public NuGet.org feed unless you intend to make your server publicly discoverable.

```bash
dotnet nuget push bin/Release/*.nupkg --api-key <your-api-key> --source https://int.nugettest.org/v3/index.json
```

The official quickstart suggests using the **NuGet test environment** `int.nugettest.org` before publishing to production. Replace `--source` with your own internal feed if you have one. Use the `--api-key` option to authenticate; generate an API key from the feed you target.

#### 8.5 Consume the package

Once your package is pushed to a NuGet feed, developers (or you) can install it in VS Code without running the server manually:

1. Visit the feed (e.g. NuGet.org) and search for packages of type `mcpserver`.
2. Open the package’s details page and copy the **MCP Server** configuration snippet that NuGet generates for VS Code.
3. Add that snippet to your workspace’s `.vscode/mcp.json`. VS Code will download the package automatically and prompt for required inputs when first used.

Because our workshop package is not meant for public consumption, share the package file (`.nupkg`) or host it on an internal NuGet server. Participants can then add the feed URL to their `nuget.config` or install the server package manually.

## Conclusion

This unified guide has covered the theory behind the Model Context Protocol, the practical steps for implementing an MCP server and client with the official .NET SDK, and instructions for integrating with both OpenAI’s cloud models and local models via Ollama. It also shows how to validate and exercise your tools in MCP Inspector and try them directly inside VS Code’s Agent Mode. Finally, the last two chapters walk through adding a production-style **Azure DevOps test-results tool** (with required inputs and structured outputs) and **containerizing the MCP server with Docker** for private, reproducible local runs. By following the steps and code examples provided here, you can build a robust workshop or project that demonstrates how large-language models can leverage external tools while maintaining context and safety.
