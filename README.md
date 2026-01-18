# WebSocketExtensions DotNetCore
Defines WebSocket extensions to microsoft WebSocket implementation to bring it in line with something like WebSocketSharp

## Installation

### Core (Client & HttpListener)
```bash
dotnet add package WebSocketExtensions
```

### Kestrel Server Support
```bash
dotnet add package WebSocketExtensions.Kestrel
```

## This package attempts to be like WebSocketSharp for DotNetCore
- It is continuously a work in progress
- It is used in heavy-use production
- It is not a perfect drop in replacement
    - Uses Async Api + Tasks
    - Uses Microsoft's own implemenation of WebSocket and HttpServer for upgrade
- It is available via nuget package websocketextensions
- See IntegrationTests for usage examples: [https://github.com/maxfridbe/websocketextensions/blob/master/src/WebSocketExtensions.Tests/IntegrationTests_Kestrel.cs]
## Usage Examples

### 1. SSL / WSS (Secure WebSockets)

#### Basic SSL (Server)
Provide a certificate directly to the constructor for automatic HTTPS/WSS configuration.
```csharp
var cert = new X509Certificate2("certificate.pfx", "password");
using var server = new KestrelWebSocketServer(logger, certificate: cert);

// Server logs an error if cert CN doesn't match 'localhost'
await server.StartAsync("https://localhost:5001/");
```

#### Custom SSL/Kestrel Configuration (Server)
Use the `configureKestrel` callback for advanced scenarios like requiring client certificates or specific protocols.
```csharp
using var server = new KestrelWebSocketServer(logger, configureKestrel: options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.ServerCertificate = new X509Certificate2("cert.pfx", "password");
        httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    });
    options.Limits.MaxConcurrentConnections = 100;
});
await server.StartAsync("https://localhost:5001/");
```

#### Connecting to WSS (Client)
Configure validation if using self-signed certificates.
```csharp
using var client = new WebSocketClient();
client.ConfigureOptionsBeforeConnect = options => 
{
    // For self-signed certs in development
    options.RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
};
await client.ConnectAsync("wss://localhost:5001/path");
```

### 2. Binary Messages

#### Receiving Binary (Server)
```csharp
public class MyBinaryBehavior : KestrelWebSocketServerBehavior {
    public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e) {
        // e.Data is the byte[]
        _logger.LogInformation($"Received {e.Data.Length} bytes from {e.ConnectionId}");
    }
}
```

#### Sending Binary (Client)
```csharp
// Send byte array
await client.SendBytesAsync(new byte[] { 0x42, 0x24 });

// Send a Stream (efficient for large files)
using var fs = File.OpenRead("data.bin");
await client.SendStreamAsync(fs);
```

### 3. Text Messages

#### Handling Text (Server)
```csharp
server.AddRouteBehavior("/chat", () => new ChatBehavior {
    OnStringMessage = (e) => {
        _logger.LogInformation($"User said: {e.Data}");
        e.WebSocket.SendStringAsync("ACK: " + e.Data);
    }
});
```

#### Handling Text (Client)
```csharp
var client = new WebSocketClient();
client.MessageHandler = (e) => 
{
    Console.WriteLine($"Server sent: {e.Data}");
};
await client.ConnectAsync("ws://localhost:8080/chat");
await client.SendStringAsync("Hello!");
```

---
## Why not use WebSocketSharp
- They do not seem to have a dotnetcore version
    - They Seem to be selling a version that is compatible in UnityStore (not open source)
- People have taken it and forked it and fixed 80% of issues with it
    - The forks have poor drop-in compatiblity
    - Most dont work with DotNetCore
    - Questionable Integration Tests
    - Most not touched in forever
---
## This Nuget Package (websocketextensions) defines
- KestrelWebSocketServer (for kestrel)
- HttpListenerWebSocketServer [Obsolete] (for http.sys (linux + windows) for now)
- WebListenerWebSocketServer [Obsolete] (for windows http.sys)
- WebSocketClient
- Extension Methods to WebSocket which allow for things like SendStream

## Roadmap
- Ability to use new Pipe feature to stream bytes directly to handler
- More Exahstive unit tests

## Liscensed under MIT
