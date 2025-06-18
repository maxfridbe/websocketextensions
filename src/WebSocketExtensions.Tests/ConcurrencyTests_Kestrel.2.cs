using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
      
        #region Concurrent Connection Management Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ConnectDuringLoad_ShouldAllowNewConnections()
        {
            // Arrange
            int initialClientCount = 50;
            int loadClientCount = 50;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");
            var initialClients = new List<WebSocketClient>();
            var cts = new CancellationTokenSource();

            // Connect initial clients
            for (int i = 0; i < initialClientCount; i++)
            {
                var client = new WebSocketClient();
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                initialClients.Add(client);
            }

            // Start a task to generate load from initial clients
            var loadTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var sendTasks = initialClients.Select(c => c.SendStringAsync("load")).ToList();
                    await Task.WhenAll(sendTasks);
                    await Task.Delay(50, cts.Token);
                }
            });

            // Act
            // While the server is under load, connect new clients
            var loadClients = new List<WebSocketClient>();
            var connectTasks = new List<Task>();
            for (int i = 0; i < loadClientCount; i++)
            {
                var client = new WebSocketClient();
                loadClients.Add(client);
                connectTasks.Add(client.ConnectAsync($"ws://localhost:{port}/ws"));
            }
            await Task.WhenAll(connectTasks);
            await Task.Delay(500); // Allow connections to register

            // Assert
            int totalClients = initialClientCount + loadClientCount;
            Assert.Equal(totalClients, server.GetActiveConnectionIds().Count);
            Assert.Equal(totalClients, behavior.Connections.Count);

            // Cleanup
            cts.Cancel();
            await loadTask.ContinueWith(t => { });
            foreach (var client in initialClients.Concat(loadClients))
            {
                client.Dispose();
            }
        }
        
        #endregion

        #region Concurrent Message Sending Tests
        
        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task HandlerExceptions_ShouldNotCrashServer()
        {
            // Arrange
            int clientCount = 50;
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var receivedGoodMessages = new ConcurrentBag<string>();
            var behavior = new TestBehavior
            {
                OnStringMessageAction = (e) =>
                {
                    if (e.Data == "bad")
                    {
                        throw new InvalidOperationException("This is a test exception.");
                    }
                    receivedGoodMessages.Add(e.Data);
                    return Task.CompletedTask;
                }
            };
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
            // First, send messages that will cause exceptions concurrently
            var badSendTasks = clients.Select(c => c.SendStringAsync("bad")).ToList();
            await Task.WhenAll(badSendTasks);
            
            await Task.Delay(200); // Give time for exceptions to be processed

            // Now, send good messages to ensure the server is still responsive
            var goodSendTasks = clients.Select(c => c.SendStringAsync("good")).ToList();
            await Task.WhenAll(goodSendTasks);
            
            await Task.Delay(500);

            // Assert
            Assert.Equal(clientCount, server.GetActiveConnectionIds().Count); // All clients should still be connected
            Assert.Equal(clientCount, receivedGoodMessages.Count); // All "good" messages should have been received

            // Cleanup
            foreach (var client in clients) client.Dispose();
        }
        
        #endregion

        #region Concurrent State & Queue Management Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task ConcurrentAbortAndSend_ShouldNotDeadlock()
        {
            // Arrange
            var logger = LoggerFac();
            using var server = new KestrelWebSocketServer(logger);
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");

            using var client = new WebSocketClient();
            await client.ConnectAsync($"ws://localhost:{port}/ws");
            var connectionId = server.GetActiveConnectionIds().Single();
            var sw = new Stopwatch();

            // Act
            sw.Start();
            var abortTask = Task.Run(() => server.AbortConnectionAsync(connectionId));
            var sendTask = Task.Run(async () =>
            {
                // Send messages in a loop until the connection is closed.
                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        await server.SendStringAsync(connectionId, $"Message {i}");
                    }
                    catch
                    {
                        // Exceptions are expected as the connection is being closed.
                        break;
                    }
                }
            });

            var testTask = Task.WhenAll(abortTask, sendTask);
            var completedTask = await Task.WhenAny(testTask, Task.Delay(2000)); // 2 second timeout
            sw.Stop();

            // Assert
            Assert.Equal(testTask, completedTask); // The test should complete and not time out.
            Assert.True(sw.ElapsedMilliseconds < 2000, "Test should complete quickly without deadlocking.");
        }
        
        #endregion

        #region Concurrent Server Lifecycle & Error Handling Tests

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task DisposeUnderFullLoad_ShouldDisconnectAllClients()
        {
            // Arrange
            int clientCount = 100;
            var logger = LoggerFac();
            var server = new KestrelWebSocketServer(logger); // Don't use 'using' as we manually dispose
            var behavior = new TestBehavior();
            var port = FreeTcpPort();
            server.AddRouteBehavior("/ws", () => behavior);
            await server.StartAsync($"http://localhost:{port}/");

            var clients = new List<WebSocketClient>();
            var closeTcsList = new List<TaskCompletionSource<bool>>();
            
            for (int i = 0; i < clientCount; i++)
            {
                var tcs = new TaskCompletionSource<bool>();
                var client = new WebSocketClient();
                client.CloseHandler = e => tcs.TrySetResult(true);
                await client.ConnectAsync($"ws://localhost:{port}/ws");
                clients.Add(client);
                closeTcsList.Add(tcs);
            }
            
            var cts = new CancellationTokenSource();
            var loadTask = Task.Run(async () => {
                while (!cts.IsCancellationRequested)
                {
                    await Task.WhenAll(clients.Select(c => c.SendStringAsync("load")));
                    await Task.Delay(50, cts.Token);
                }
            });

            // Act
            await Task.Delay(200); // Let some load build up
            server.Dispose();

            // Assert
            var allClosedTask = Task.WhenAll(closeTcsList.Select(tcs => tcs.Task));
            var completedTask = await Task.WhenAny(allClosedTask, Task.Delay(5000));
            
            Assert.Equal(allClosedTask, completedTask); // All clients should have been closed.
            Assert.True(allClosedTask.IsCompletedSuccessfully);

            // Cleanup
            cts.Cancel();
            await loadTask.ContinueWith(t => { });
            foreach(var client in clients) client.Dispose();
        }
        
        #endregion
    }
}
