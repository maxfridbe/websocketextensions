using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebSocketExtensions.Kestrel;
using Xunit;
using Xunit.Abstractions;

namespace WebSocketExtensions.Tests
{
    public class IntegrationTests_Kestrel_More
    {
        private readonly ITestOutputHelper _output;

        public IntegrationTests_Kestrel_More(ITestOutputHelper output)
        {
            _output = output;
        }

        // Helper to get a free TCP port
        private static int FreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        // Helper to create a logger
        private ILogger LoggerFac()
        {
            var loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder
                                .SetMinimumLevel(LogLevel.Trace)
                                .AddXUnit(_output)); // Pipe logs to XUnit output for debugging

            return loggerFactory.CreateLogger<IntegrationTests_Kestrel_More>();
        }

        // A customizable behavior for testing
        public class TestBeh : KestrelWebSocketServerBehavior
        {
            public Func<HttpContext, bool> OnValidateContextHandler { get; set; } = (ctx) => true;
            public Action<StringMessageReceivedEventArgs> StringMessageHandler { get; set; } = (_) => { };
            public Action<BinaryMessageReceivedEventArgs> BinaryMessageHandler { get; set; } = (_) => { };
            public Action<WebSocketClosedEventArgs> ClosedHandler { get; set; } = (_) => { };
            public Action<Guid, HttpContext> ConnectionEstablishedHandler { get; set; } = (_, _) => { };

            public override bool OnValidateContext(HttpContext listenerContext, ref int errStatusCode, ref string statusDescription)
            {
                if (!OnValidateContextHandler(listenerContext))
                {
                    errStatusCode = 401;
                    statusDescription = "Unauthorized";
                    return false;
                }
                return true;
            }

            public override void OnConnectionEstablished(Guid connectionId, HttpContext requestContext)
            {
                ConnectionEstablishedHandler(connectionId, requestContext);
            }

            public override void OnStringMessage(StringMessageReceivedEventArgs e)
            {
                StringMessageHandler(e);
            }

            public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e)
            {
                BinaryMessageHandler(e);
            }

            public override void OnClose(WebSocketClosedEventArgs e)
            {
                ClosedHandler(e);
            }
        }

        /// <summary>
        /// Tests that a client connection is rejected if the OnValidateContext behavior returns false.
        /// </summary>
        [Fact]
        public async Task TestValidationFailure_RejectsConnection()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var port = FreeTcpPort();

            var beh = new TestBeh
            {
                OnValidateContextHandler = (ctx) => false // Always reject
            };

            server.AddRouteBehavior("/test", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var client = new ClientWebSocket();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<WebSocketException>(() =>
                client.ConnectAsync(new Uri($"ws://localhost:{port}/test"), CancellationToken.None));

            // logger.LogError(ex.ToString());
            // Should fail with a server error, as the upgrade response will not be 101.
             Assert.Contains("The server returned status code '401' when status code '101' was expected.", ex.Message);
        }

        /// <summary>
        /// Tests that the server can handle multiple routes with different behaviors.
        /// </summary>
        [Fact]
        public async Task TestMultipleRoutes_WithDifferentBehaviors()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var port = FreeTcpPort();

            var tcs1 = new TaskCompletionSource<string>();
            var tcs2 = new TaskCompletionSource<string>();

            var beh1 = new TestBeh { StringMessageHandler = (e) => tcs1.SetResult(e.Data + "_route1") };
            var beh2 = new TestBeh { StringMessageHandler = (e) => tcs2.SetResult(e.Data + "_route2") };

            server.AddRouteBehavior("/route1", () => beh1);
            server.AddRouteBehavior("/route2", () => beh2);
            await server.StartAsync($"http://localhost:{port}/");

            var client1 = new WebSocketClient();
            var client2 = new WebSocketClient();

            await client1.ConnectAsync($"ws://localhost:{port}/route1");
            await client2.ConnectAsync($"ws://localhost:{port}/route2");

            // Act
            await client1.SendStringAsync("hello");
            await client2.SendStringAsync("world");

            var res1 = await tcs1.Task;
            var res2 = await tcs2.Task;

            // Assert
            Assert.Equal("hello_route1", res1);
            Assert.Equal("world_route2", res2);
        }

        /// <summary>
        /// Tests the GetStats method to ensure it correctly reports client count.
        /// </summary>
        [Fact]
        public async Task TestGetStats_ReturnsCorrectClientCount()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var port = FreeTcpPort();
            server.AddRouteBehavior("/stats", () => new TestBeh());
            await server.StartAsync($"http://localhost:{port}/");

            var stats1 = server.GetStats();
            Assert.Equal(0, stats1.ClientCount);

            // Act
            var client1 = new WebSocketClient();
            await client1.ConnectAsync($"ws://localhost:{port}/stats");
            var stats2 = server.GetStats();
            Assert.Equal(1, stats2.ClientCount);

            var client2 = new WebSocketClient();
            await client2.ConnectAsync($"ws://localhost:{port}/stats");
            var stats3 = server.GetStats();
            Assert.Equal(2, stats3.ClientCount);

            client1.Dispose();
            await Task.Delay(200); // Give server time to process close

            // Assert
            var stats4 = server.GetStats();
            Assert.Equal(1, stats4.ClientCount);
        }

        /// <summary>
        /// Tests that sending a message to a client that has already disconnected throws an exception.
        /// </summary>
        [Fact]
        public async Task TestSendToDisconnectedClient_ThrowsException()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var port = FreeTcpPort();
            Guid connectionId = Guid.Empty;
            var connectedTcs = new TaskCompletionSource<bool>();
            var disconnectedTcs = new TaskCompletionSource<bool>();

            var beh = new TestBeh
            {
                ConnectionEstablishedHandler = (id, ctx) =>
                {
                    connectionId = id;
                    connectedTcs.SetResult(true);
                },
                ClosedHandler = (e) => disconnectedTcs.SetResult(true)
            };

            server.AddRouteBehavior("/test", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var client = new WebSocketClient();
            await client.ConnectAsync($"ws://localhost:{port}/test");
            await connectedTcs.Task;

            // Act
            client.Dispose(); // Disconnect the client
            await disconnectedTcs.Task; // Wait for the server to recognize the disconnection

            // Assert
            await Assert.ThrowsAsync<Exception>(() =>
                server.SendStringAsync(connectionId, "you still there?"));
        }

        /// <summary>
        /// Tests that a custom ping handler can be configured and is correctly invoked.
        /// </summary>
        [Fact]
        public async Task TestCustomPingHandler()
        {
            // Arrange
            var customResponse = new { Status = "CustomOK", Timestamp = DateTime.UtcNow };
            var pingHandler = async (HttpContext context, KestrelWebSocketServerStats stats) =>
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(customResponse));
            };

            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger, httpPingResponseRoute: "/customping", pingHandler: pingHandler);
            var port = FreeTcpPort();
            await server.StartAsync($"http://localhost:{port}/");

            var httpClient = new HttpClient();

            // Act
            var response = await httpClient.GetAsync($"http://localhost:{port}/customping");
            var responseBody = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("CustomOK", responseObject.GetProperty("Status").GetString());
            Assert.True(responseObject.TryGetProperty("Timestamp", out _));
        }

        /// <summary>
        /// Tests the behavior when a large number of clients connect and disconnect concurrently.
        /// </summary>
        [Fact]
        public async Task TestHighConcurrency_ConnectDisconnect()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var port = FreeTcpPort();
            server.AddRouteBehavior("/concurrent", () => new TestBeh());
            await server.StartAsync($"http://localhost:{port}/");

            const int clientCount = 100;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < clientCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var client = new WebSocketClient();
                        await client.ConnectAsync($"ws://localhost:{port}/concurrent");
                        await Task.Delay(50); // stay connected briefly
                        client.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Client task failed: {ex.Message}");
                        Assert.Fail("Client connection/disconnection failed.");
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            // Give server a moment to process all disconnections
            await Task.Delay(500);
            var stats = server.GetStats();
            Assert.Equal(0, stats.ClientCount);
        }

        /// <summary>
        /// Tests the queueStringMessages feature by having a handler that is slow,
        /// which should cause messages to queue up.
        /// </summary>
        [Fact]
        public async Task TestQueueStringMessages_WithSlowHandler()
        {
            // Arrange
            var logger = LoggerFac();
            int messagesProcessed = 0;
            var allMessagesProcessedTcs = new TaskCompletionSource<bool>();

            var beh = new TestBeh
            {
                StringMessageHandler = (e) =>
                {
                    Thread.Sleep(2000); // Simulate slow processing
                    Interlocked.Increment(ref messagesProcessed);
                    if (messagesProcessed == 10)
                    {
                        allMessagesProcessedTcs.SetResult(true);
                    }
                }
            };
            
            // Enable string message queuing
            using var server = new KestrelWebSocketServer(logger, queueStringMessages: true);
            var port = FreeTcpPort();
            server.AddRouteBehavior("/queue", () => beh);
            await server.StartAsync($"http://localhost:{port}/");

            var client = new WebSocketClient();
            await client.ConnectAsync($"ws://localhost:{port}/queue");
            
            // Act
            var sendTasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                sendTasks.Add(client.SendStringAsync($"Message {i}"));
            }
            await Task.WhenAll(sendTasks);

            var statsBeforeProcessing = server.GetStats();
            _output.WriteLine($"Queue count immediately after sending: {statsBeforeProcessing.QueueStats.Count}");

           
            // Wait for all messages to be processed.
            await allMessagesProcessedTcs.Task;
            Assert.Equal(10, messagesProcessed);

            await Task.Delay(100); // Give queue time to settle
            var statsAfterProcessing = server.GetStats();
            Assert.Equal(0, statsAfterProcessing.QueueStats.Count);
        }
    }
}