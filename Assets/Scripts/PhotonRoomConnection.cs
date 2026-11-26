using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

sealed class PhotonRoomConnection : IDisposable
{
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly CancellationTokenSource stop = new CancellationTokenSource();
    readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
    readonly SemaphoreSlim queued = new SemaphoreSlim(0);
    readonly Action<string, int, string> publish;
    readonly int session;
    bool disposed;

    public PhotonRoomConnection(Uri address, int session, Action<string, int, string> publish)
    {
        this.session = session;
        this.publish = publish;
        _ = Run(address);
    }

    public void Send(string message)
    {
        if (disposed || socket.State != WebSocketState.Open) return;
        // Pose traffic is expendable if a slow peer stops draining the socket.
        if (outgoing.Count >= 24 && message.Contains("\"type\":\"pose\"")) return;
        outgoing.Enqueue(message);
        queued.Release();
    }

    async Task Run(Uri address)
    {
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await socket.ConnectAsync(address, timeout.Token).ConfigureAwait(false);
            }
            publish("open", session, "");
            var send = SendMessages();
            var receive = ReceiveMessages();
            var first = await Task.WhenAny(send, receive).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            stop.Cancel();
            try { await Task.WhenAll(send, receive).ConfigureAwait(false); }
            catch (OperationCanceledException) {}
        }
        catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is IOException)
        {
            if (!stop.IsCancellationRequested) publish("error", session, ex.Message);
        }
        finally
        {
            stop.Cancel();
            socket.Dispose();
            publish("close", session, "");
        }
    }

    async Task SendMessages()
    {
        while (!stop.IsCancellationRequested)
        {
            await queued.WaitAsync(stop.Token).ConfigureAwait(false);
            if (outgoing.TryDequeue(out var message))
                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
                    WebSocketMessageType.Text, true, stop.Token).ConfigureAwait(false);
        }
    }

    async Task ReceiveMessages()
    {
        var buffer = new ArraySegment<byte>(new byte[16384]);
        using var text = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, stop.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
            text.Write(buffer.Array, 0, result.Count);
            if (text.Length > 1024 * 1024) throw new IOException("房间消息超过 1 MB");
            if (!result.EndOfMessage) continue;
            if (result.MessageType == WebSocketMessageType.Text)
                publish("message", session, Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length));
            text.SetLength(0);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
    }
}
