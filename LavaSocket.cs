using System;
using Discord;
using Newtonsoft.Json;
using PureWebSockets;
using Victoria.Payloads;
using System.Threading;
using System.Net.WebSockets;
using System.Collections.Generic;

namespace Victoria
{
    public sealed class LavaSocket
    {
        private int Tries;
        private int MaxTries;
        private bool IsDisposed;
        private Endpoint Socket;
        internal event Action<LogSeverity, string> Log;
        internal PureWebSocket PureSocket { get; set; }
        internal bool IsConnected => !Volatile.Read(ref IsDisposed);

        internal void Connect()
        {
            try
            {
                PureSocket.Connect();
            }
            catch
            {
                // ignored
            }
        }

        internal void Connect(LavaConfig config, ulong userId, int shards)
        {
            Socket = config.Socket;
            MaxTries = config.MaxTries;
            PureSocket = new PureWebSocket($"ws://{Socket.Host}:{Socket.Port}",
                new PureWebSocketOptions
                {
                    DisconnectWait = 5000,
                    Headers = new List<Tuple<string, string>>
                    {
                        new Tuple<string, string>("Num-Shards", $"{shards}"),
                        new Tuple<string, string>("Authorization", config.Authorization),
                        new Tuple<string, string>("User-Id", $"{userId}")
                    }
                });

            PureSocket.OnError += OnSocketError;
            PureSocket.OnClosed += OnSocketClosed;
            PureSocket.OnOpened += OnSocketOpened;
            Connect();
        }

        internal void Disconnect()
        {
            PureSocket.Disconnect();
            PureSocket.Dispose(true);
            Volatile.Write(ref IsDisposed, true);
        }

        internal void SendPayload(LavaPayload load)
        {
            PureSocket.Send(JsonConvert.SerializeObject(load));
            Log?.Invoke(LogSeverity.Verbose, $"Sent {load.Operation} payload.");
        }

        private void OnSocketClosed(WebSocketCloseStatus reason)
        {
            if (Tries >= MaxTries && MaxTries != 0) return;
            if (IsConnected && reason != WebSocketCloseStatus.EndpointUnavailable && (int) reason != -1)
            {
                Log?.Invoke(LogSeverity.Warning, "Websocket connection broken. Re-establishing connection...");
                try
                {
                    Interlocked.Increment(ref Tries);
                    Connect();
                }
                catch
                {
                    // ignored
                }
            }
            else if (reason != WebSocketCloseStatus.EndpointUnavailable && (int) reason != -1)
            {
                Interlocked.Increment(ref Tries);
                Log?.Invoke(LogSeverity.Warning, "Connection has been closed.");
            }
            else
            {
                Interlocked.Increment(ref Tries);
                Log?.Invoke(LogSeverity.Critical, "Lavalink is unreachable.");
            }
        }

        private void OnSocketOpened()
        {
            Tries = 0;
            Volatile.Write(ref IsDisposed, false);
            Log?.Invoke(LogSeverity.Verbose, "Websocket connection opened.");
        }

        private void OnSocketError(Exception ex)
            => Log?.Invoke(LogSeverity.Error, $"{ex.Message}\n{ex.StackTrace}");
    }
}