# WebSocketExtensions DotNetCore
Defines WebSocket extensions to microsoft WebSocket implementation to bring it in line with something like WebSocketSharp

## This package attempts to be like WebSocketSharp for DotNetCore
- It is continuously a work in progress
- It is used in heavy-use production
- It is not a perfect drop in replacement
    - Uses Async Api + Tasks
    - Uses Microsoft's own implemenation of WebSocket and HttpServer for upgrade
- It is available via nuget package websocketextensions
- See IntegrationTests for usage examples: [https://github.com/maxfridbe/websocketextensions/blob/master/src/WebSocketExtensions.Tests/IntegrationTests_WebListener.cs]
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
- HttpListenerWebSocketServer (for http.sys)
- WebListenerWebSocketServer (for core)
- WebSocketClient
- Extension Methods to WebSocket which allow for things like SendStream

## Roadmap
- Ability to use new Pipe feature to stream bytes directly to handler
- More Exahstive unit tests

## Liscensed under MIT