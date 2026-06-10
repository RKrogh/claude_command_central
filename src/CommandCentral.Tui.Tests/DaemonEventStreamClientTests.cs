using System.Net.WebSockets;
using System.Text;
using CommandCentral.Tui.Services;

namespace CommandCentral.Tui.Tests;

public class DaemonEventStreamClientTests
{
    [Fact]
    public async Task ReceiveTextMessage_FragmentSplitsMultiByteChar_DecodesIntact()
    {
        // 2- and 3-byte UTF-8 sequences, fragmented every 7 bytes so the
        // boundaries routinely land inside a character. Decoding fragments
        // individually would yield U+FFFD replacement chars.
        var text = "{\"kind\":\"instance\",\"message\":\"" +
                   string.Concat(Enumerable.Repeat("åäö€", 100)) + "\"}";
        var socket = new ScriptedWebSocket(Fragment(text, fragmentSize: 7));

        var received = await DaemonEventStreamClient.ReceiveTextMessageAsync(
            socket, new byte[16], CancellationToken.None);

        Assert.Equal(text, received);
    }

    [Fact]
    public async Task ReceiveTextMessage_SingleFragment_Decodes()
    {
        var socket = new ScriptedWebSocket([(Encoding.UTF8.GetBytes("{\"kind\":\"daemon\"}"), true)]);

        var received = await DaemonEventStreamClient.ReceiveTextMessageAsync(
            socket, new byte[64], CancellationToken.None);

        Assert.Equal("{\"kind\":\"daemon\"}", received);
    }

    [Fact]
    public async Task ReceiveTextMessage_CloseFrame_ReturnsNull()
    {
        var socket = new ScriptedWebSocket([]);

        var received = await DaemonEventStreamClient.ReceiveTextMessageAsync(
            socket, new byte[64], CancellationToken.None);

        Assert.Null(received);
    }

    private static List<(byte[] Bytes, bool EndOfMessage)> Fragment(string text, int fragmentSize)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var fragments = new List<(byte[], bool)>();

        for (var offset = 0; offset < bytes.Length; offset += fragmentSize)
        {
            var length = Math.Min(fragmentSize, bytes.Length - offset);
            fragments.Add((bytes[offset..(offset + length)], offset + length == bytes.Length));
        }

        return fragments;
    }

    /// <summary>
    /// WebSocket fake that replays a fixed list of text fragments, then a
    /// close frame.
    /// </summary>
    private sealed class ScriptedWebSocket(List<(byte[] Bytes, bool EndOfMessage)> fragments) : WebSocket
    {
        private int _index;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State =>
            _index <= fragments.Count ? WebSocketState.Open : WebSocketState.CloseReceived;
        public override string? SubProtocol => null;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_index >= fragments.Count)
            {
                _index++;
                return Task.FromResult(new WebSocketReceiveResult(
                    0, WebSocketMessageType.Close, true,
                    WebSocketCloseStatus.NormalClosure, null));
            }

            var (bytes, endOfMessage) = fragments[_index++];
            Assert.True(bytes.Length <= buffer.Count, "test fragment larger than receive buffer");
            bytes.CopyTo(buffer.Array!, buffer.Offset);

            return Task.FromResult(new WebSocketReceiveResult(
                bytes.Length, WebSocketMessageType.Text, endOfMessage));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Abort()
        {
        }

        public override void Dispose()
        {
        }
    }
}
