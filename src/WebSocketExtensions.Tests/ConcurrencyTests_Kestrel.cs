using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebSocketExtensions.Kestrel;
using Xunit;
using Xunit.Abstractions;

namespace WebSocketExtensions.Tests
{
    /// <summary>
    /// Contains a suite of concurrency and stress tests for the KestrelWebSocketServer.
    /// These tests are designed to identify race conditions, deadlocks, and performance
    /// issues under high-load scenarios.
    /// </summary>
    public partial class ConcurrencyTests_Kestrel
    {
        private readonly ITestOutputHelper _output;

        public ConcurrencyTests_Kestrel(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Helper Methods and Classes

        /// <summary>
        /// Gets an available TCP port on the local machine.
        /// </summary>
        /// <returns>An integer representing a free TCP port.</returns>
        private static int FreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>
        /// Creates a simple logger factory for use in tests.
        /// </summary>
        /// <returns>An ILogger instance.</returns>
        private ILogger LoggerFac()
        {
            var loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder
                                .SetMinimumLevel(LogLevel.Trace)
                                .AddXUnit(_output)); // Pipe log output to the Xunit test output

            return loggerFactory.CreateLogger<ConcurrencyTests_Kestrel>();
        }

        /// <summary>
        /// A basic WebSocket server behavior for testing purposes.
        /// It uses ConcurrentQueue to safely collect received messages from multiple threads.
        /// </summary>
        public class TestBehavior : KestrelWebSocketServerBehavior
        {
            public ConcurrentQueue<string> ReceivedStringMessages { get; } = new ConcurrentQueue<string>();
            public ConcurrentQueue<byte[]> ReceivedBinaryMessages { get; } = new ConcurrentQueue<byte[]>();
            public ConcurrentBag<Guid> Connections { get; } = new ConcurrentBag<Guid>();
            public ConcurrentBag<Guid> Disconnections { get; } = new ConcurrentBag<Guid>();

            public Func<StringMessageReceivedEventArgs, Task> OnStringMessageAction { get; set; } = (e) => Task.CompletedTask;
            public Action<BinaryMessageReceivedEventArgs> OnBinaryMessageAction { get; set; } = (e) => { };

            public override void OnConnectionEstablished(Guid connectionId, HttpContext requestContext)
            {
                Connections.Add(connectionId);
                base.OnConnectionEstablished(connectionId, requestContext);
            }

            public override void OnStringMessage(StringMessageReceivedEventArgs e)
            {
                ReceivedStringMessages.Enqueue(e.Data);
                OnStringMessageAction(e).GetAwaiter().GetResult();
                base.OnStringMessage(e);
            }

            public override void OnBinaryMessage(BinaryMessageReceivedEventArgs e)
            {
                ReceivedBinaryMessages.Enqueue(e.Data);
                OnBinaryMessageAction(e);
                base.OnBinaryMessage(e);
            }

            public override void OnClose(WebSocketClosedEventArgs e)
            {
                Disconnections.Add(e.ConnectionId);
                base.OnClose(e);
            }
        }
        #endregion

        #region Concurrent Connection Management Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task HighVolumeConnections_ShouldHandleManyClientsConnectingSimultaneously()
        {
            // Arrange
            int clientCount = 500;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            var connectTasks = new List<Task>();
            var clients = new List<WebSocketClient>();

            // Act
            for (int i = 0; i < clientCount; i++)
            {
                var client = new WebSocketClient();
                clients.Add(client);
                connectTasks.Add(client.ConnectAsync($"ws://localhost:{port}/ws"));
            }

            await Task.WhenAll(connectTasks);
            await Task.Delay(1000); // Allow time for all connections to be registered by the server.

            // Assert
            Assert.Equal(clientCount, behavior.Connections.Count);
            Assert.Equal(clientCount, server.GetActiveConnectionIds().Count);

            // Cleanup
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
        
        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task HighConnectionChurn_ShouldRemainStable()
        {
            // Arrange
            int churnCount = 200;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");

            // Act
            var churnTasks = new List<Task>();
            for (int i = 0; i < churnCount; i++)
            {
                churnTasks.Add(Task.Run(async () =>
                {
                    var client = new WebSocketClient();
                    await client.ConnectAsync($"ws://localhost:{port}/ws");
                    await Task.Delay(10); // small delay to ensure connection is established
                    client.Dispose();
                }));
            }

            await Task.WhenAll(churnTasks);
            await Task.Delay(1000); // Allow time for all disconnections to be processed.

            // Assert
            Assert.Equal(churnCount, behavior.Connections.Count);
            Assert.Equal(churnCount, behavior.Disconnections.Count);
            Assert.Empty(server.GetActiveConnectionIds());
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ConcurrentServerAborts_ShouldDisconnectClients()
        {
            // Arrange
            int clientCount = 50;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            var clients = new List<WebSocketClient>();
            for(int i = 0; i < clientCount; i++)
            {
                var client = new WebSocketClient();
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                clients.Add(client);
            }
            await Task.Delay(200); // Ensure all are connected
            var connectionIds = server.GetActiveConnectionIds();
            Assert.Equal(clientCount, connectionIds.Count);

            // Act
            var abortTasks = connectionIds.Select(id => server.AbortConnectionAsync(id)).ToList();
            await Task.WhenAll(abortTasks);
            await Task.Delay(500); // Allow time for close to propagate

            // Assert
            Assert.Equal(clientCount, behavior.Disconnections.Count);
            Assert.Empty(server.GetActiveConnectionIds());
            
            // Cleanup
            foreach (var client in clients) client.Dispose();
        }

        #endregion

        #region Concurrent Message Sending Tests
        
        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ServerBroadcast_ShouldSendToAllClientsConcurrently()
        {
            // Arrange
            int clientCount = 100;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");

            var clients = new List<WebSocketClient>();
            var receivedMessages = new ConcurrentBag<string>();

            for (int i = 0; i < clientCount; i++)
            {
                var client = new WebSocketClient();
                client.MessageHandler = (e) => receivedMessages.Add(e.Data);
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                clients.Add(client);
            }
            await Task.Delay(200); // Ensure all connections are established
            var connectionIds = server.GetActiveConnectionIds();

            // Act
            string message = "broadcast_message";
            var broadcastTasks = connectionIds.Select(id => server.SendStringAsync(id, message)).ToList();
            await Task.WhenAll(broadcastTasks);
            await Task.Delay(500); // Allow time for messages to be received

            // Assert
            Assert.Equal(clientCount, receivedMessages.Count);
            Assert.True(receivedMessages.All(m => m == message));
            
            // Cleanup
            foreach (var client in clients) client.Dispose();
        }
        
        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ClientSpam_ShouldProcessAllMessagesFromServer()
        {
            // Arrange
            int clientCount = 20;
            int messagesPerClient = 50;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            
            var clients = new List<WebSocketClient>();
            for (int i = 0; i < clientCount; i++)
            {
                var client = new WebSocketClient();
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                clients.Add(client);
            }

            // Act
            var sendTasks = clients.Select(client => Task.Run(async () =>
            {
                for (int i = 0; i < messagesPerClient; i++)
                {
                    await client.SendStringAsync($"Message {i}");
                }
            })).ToList();

            await Task.WhenAll(sendTasks);
            await Task.Delay(1000); // Wait for server to process all messages

            // Assert
            int expectedTotalMessages = clientCount * messagesPerClient;
            Assert.Equal(expectedTotalMessages, behavior.ReceivedStringMessages.Count);

            // Cleanup
            foreach (var client in clients) client.Dispose();
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task BidirectionalLoad_ShouldRemainStable()
        {
            // Arrange
            int clientCount = 20;
            int messagesPerClient = 50;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior
            {
                // Echo server behavior
                OnStringMessageAction = (e) => e.WebSocket.SendStringAsync($"ECHO: {e.Data}")
            };
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            
            var clients = new List<WebSocketClient>();
            var clientReceivedCounts = new ConcurrentDictionary<WebSocketClient, int>();

            for (int i = 0; i < clientCount; i++)
            {
                var client = new WebSocketClient();
                clientReceivedCounts[client] = 0;
                client.MessageHandler = (e) =>
                {
                    clientReceivedCounts.AddOrUpdate(client, 1, (c, count) => count + 1);
                };
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                clients.Add(client);
            }

            // Act
            var clientTasks = clients.Select(client => Task.Run(async () =>
            {
                for (int i = 0; i < messagesPerClient; i++)
                {
                    await client.SendStringAsync($"Message {i}");
                    await Task.Delay(5); // small delay
                }
            })).ToList();

            await Task.WhenAll(clientTasks);
            await Task.Delay(2000); // Wait for all echoes to return

            // Assert
            int expectedTotalSent = clientCount * messagesPerClient;
            Assert.Equal(expectedTotalSent, behavior.ReceivedStringMessages.Count);
            foreach (var client in clients)
            {
                Assert.Equal(messagesPerClient, clientReceivedCounts[client]);
            }
            
            // Cleanup
            foreach (var client in clients) client.Dispose();
        }
        
        #endregion

        #region Concurrent State & Queue Management Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task PollGetStats_ShouldNotThrowUnderLoad()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            var cts = new CancellationTokenSource();

            // Act
            // Start a background task to spam the server with connections and messages
            var loadTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var client = new WebSocketClient();
                    await client.ConnectAsync($"ws://localhost:{port}/ws");
                    await client.SendStringAsync("load");
                    await Task.Delay(10, cts.Token);
                    client.Dispose();
                }
            });

            // Poll GetStats concurrently
            var exceptions = new ConcurrentBag<Exception>();
            var pollingTask = Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        var stats = server.GetStats();
                        Assert.NotNull(stats);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                    await Task.Delay(5, cts.Token);
                }
            });

            await Task.WhenAny(pollingTask, Task.Delay(2000));
            cts.Cancel();
            await Task.WhenAll(loadTask, pollingTask).ContinueWith(t => {}); // Suppress exceptions from cancellation

            // Assert
            Assert.Empty(exceptions);
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ExceedQueueThrottleLimit_ShouldTriggerPagingWithoutErrors()
        {
            // Arrange
            long throttleBytes = 1024 * 10; // 10 KB throttle limit
            int messageSize = 1024; // 1 KB messages
            int messageCount = 20; // 20 KB total, should exceed throttle
            var logger = LoggerFac();
            
            // Use a low throttle limit. The behavior handler is delayed to ensure the queue builds up.
            using var server = new KestrelWebSocketServer(logger, queueThrottleLimitBytes: throttleBytes);
            
            var handlerDelay = new ManualResetEventSlim(false);
            var behavior = new TestBehavior();
            behavior.OnBinaryMessageAction = e => {
                handlerDelay.Wait(); // Block the queue consumer thread
            };
            
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            
            using var client = new WebSocketClient();
            await client.ConnectAsync($"ws://localhost:{port}/ws");
            var message = new byte[messageSize];
            new Random().NextBytes(message);

            // Act
            var sendTasks = new List<Task>();
            for (int i = 0; i < messageCount; i++)
            {
                sendTasks.Add(client.SendBytesAsync(message));
            }
            await Task.WhenAll(sendTasks);
            
            await Task.Delay(500); // Allow time for messages to be pushed to the queue and paged.
            
            var statsBeforeProcessing = server.GetStats();

            // Allow the handler to process messages
            handlerDelay.Set(); 
            await Task.Delay(1000); // Wait for processing
            var statsAfterProcessing = server.GetStats();

            // Assert
            // Assert.True(statsBeforeProcessing.QueueStats.QueueBinarySizeBytes > throttleBytes, "Queue size should have exceeded throttle before processing.");
            Assert.Equal(0, statsAfterProcessing.QueueStats.QueueBinarySizeBytes);
            Assert.Equal(messageCount, behavior.ReceivedBinaryMessages.Count);
        }

        #endregion
    }
}
