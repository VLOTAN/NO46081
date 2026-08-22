using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OSAKA;

public class LocalServer
{
    private static IHost? _host;
    private static readonly List<WebSocket> _clients = new();
    private static readonly object _clientsLock = new();

    public static async Task StartAsync(string ip = "localhost", int port = 5000)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls($"http://{ip}:{port}");
                webBuilder.Configure(app =>
                {
                    app.UseWebSockets();

                    var fontsPath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "Fonts");

                    var fontFile = Path.Combine(
                        fontsPath,
                        "JF-Dot-Shinonome14B.ttf");

                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(fontsPath),
                        RequestPath = "/fonts"
                    });

                    var wwwrootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                    if (!Directory.Exists(wwwrootPath))
                    {
                        Directory.CreateDirectory(wwwrootPath);
                    }

                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(wwwrootPath),
                        RequestPath = ""
                    });

                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Path == "/ws")
                        {
                            if (context.WebSockets.IsWebSocketRequest)
                            {
                                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                await HandleWebSocket(webSocket);
                            }
                            else
                            {
                                context.Response.StatusCode = 400;
                            }
                        }
                        else if (context.Request.Path == "/")
                        {
                            context.Response.Redirect("/chat.html");
                            return;
                        }
                        else
                        {
                            await next();
                        }
                    });
                });
            })
            .Build();

        await _host.StartAsync();
    }

    private static async Task HandleWebSocket(WebSocket webSocket)
    {
        lock (_clientsLock)
        {
            _clients.Add(webSocket);
        }

        var buffer = new byte[1024 * 4];
        try
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!result.CloseStatus.HasValue)
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
        finally
        {
            lock (_clientsLock)
            {
                _clients.Remove(webSocket);
            }
        }
    }

    public static event Action<string, string, string>? OnSpecialChat;

    public static async Task BroadcastMessage(string author, string message)
    {
        await BroadcastCommand("chat", new { author, message });
    }

    public static async Task BroadcastSpecialChat(string type, string author, string message)
    {
        OnSpecialChat?.Invoke(type, author, message);
        await BroadcastCommand("specialChat", new { type, author, message });
    }

   

    public static async Task BroadcastCommand(string type, object payload)
    {
        var data = JsonSerializer.Serialize(new { type, payload });
        var bytes = Encoding.UTF8.GetBytes(data);
        var segment = new ArraySegment<byte>(bytes);

        List<WebSocket> currentClients;
        lock (_clientsLock)
        {
            currentClients = _clients.ToList();
        }

        foreach (var client in currentClients)
        {
            if (client.State == WebSocketState.Open)
            {
                await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    public static async Task StopAsync()
    {
        if (_host != null)
        {
            List<WebSocket> currentClients;
            lock (_clientsLock)
            {
                currentClients = _clients.ToList();
                _clients.Clear();
            }

            foreach (var client in currentClients)
            {
                try
                {
                    if (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived)
                    {
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None);
                    }
                }
                catch { }
            }

            await _host.StopAsync(TimeSpan.FromSeconds(1));
            _host.Dispose();
            _host = null;
        }
    }
}
