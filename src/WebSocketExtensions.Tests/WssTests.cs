using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using WebSocketExtensions.Kestrel;
using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http;

namespace WebSocketExtensions.Tests
{
    public class WssTests
    {
        private ITestOutputHelper _output;

        public WssTests(ITestOutputHelper output)
        {
            _output = output;
        }

        static int _FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private ILogger _loggerFac()
        {
            var loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder
                                .SetMinimumLevel(LogLevel.Trace)
                                .AddConsole());

            return loggerFactory.CreateLogger<WssTests>();
        }

        private X509Certificate2 GenerateSelfSignedCertificate(string commonName = "localhost")
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest($"cn={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(365));
                
                // Export and import to ensure the private key is correctly associated for Kestrel
                return new X509Certificate2(certificate.Export(X509ContentType.Pfx, "password"), "password", X509KeyStorageFlags.DefaultKeySet);
            }
        }

        public class WssTestBeh : KestrelWebSocketServerBehavior
        {
            public Action<StringMessageReceivedEventArgs> StringMessageHandler { get; set; }

            public override void OnStringMessage(StringMessageReceivedEventArgs e)
            {
                StringMessageHandler?.Invoke(e);
            }
        }

        private class TestLogger : ILogger
        {
            public List<string> LogMessages = new List<string>();
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var msg = formatter(state, exception);
                LogMessages.Add(msg);
                Console.WriteLine($"LOG [{logLevel}]: {msg}");
            }
        }

        [Fact]
        public async Task TestWssConnection_LogsErrorOnCNMismatch()
        {
            var logger = new TestLogger();
            var port = _FreeTcpPort();
            var cert = GenerateSelfSignedCertificate("mismatch");

            using var server = new KestrelWebSocketServer(logger, certificate: cert);

            server.AddRouteBehavior("/wss", () => new WssTestBeh());

            await server.StartAsync($"https://localhost:{port}/");

            using var client = new WebSocketClient()
            {
                ConfigureOptionsBeforeConnect = (options) =>
                {
                    options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                }
            };

            try
            {
                await client.ConnectAsync($"wss://localhost:{port}/wss");
            }
            catch { }

            Assert.Contains(logger.LogMessages, m => m.Contains("Certificate CN 'mismatch' does not match listener host 'localhost'"));
        }

        [Fact]
        public async Task TestWssConnection()
        {
            var logger = _loggerFac();
            var port = _FreeTcpPort();
            var cert = GenerateSelfSignedCertificate();

            using var server = new KestrelWebSocketServer(logger, configureKestrel: options =>
            {
                options.ConfigureHttpsDefaults(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = cert;
                });
            });

            var tcs = new TaskCompletionSource<string>();
            server.AddRouteBehavior("/wss", () => new WssTestBeh
            {
                StringMessageHandler = (e) =>
                {
                    e.WebSocket.SendStringAsync("echo:" + e.Data, CancellationToken.None);
                }
            });

            await server.StartAsync($"https://localhost:{port}/");

            string received = null;

            using var client = new WebSocketClient()
            {
                MessageHandler = (e) => {
                    received = e.Data;
                    tcs.TrySetResult(e.Data);
                },
                ConfigureOptionsBeforeConnect = (options) =>
                {
                    options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                }
            };

            await client.ConnectAsync($"wss://localhost:{port}/wss");
            await client.SendStringAsync("hello wss");

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            
            if (completedTask != tcs.Task)
            {
                throw new TimeoutException("Timed out waiting for echo response over WSS");
            }

            Assert.Equal("echo:hello wss", received);
        }
    }
}
