using System;
using WebSocketSharp;

namespace Parse.LiveQuery {
    public class WebSocketSharpClient : IWebSocketClient {

        public static readonly WebSocketClientFactory Factory = (hostUri, callback) => new WebSocketSharpClient(hostUri, callback);

        public IWebSocketClient CreateInstance(Uri hostUri, IWebSocketClientCallback webSocketClientCallback) {
            return new WebSocketSharpClient(hostUri, webSocketClientCallback);
        }

        private readonly Logger _log = new Logger();
        private readonly object _mutex = new object();

        private readonly Uri _hostUri;
        private readonly IWebSocketClientCallback _webSocketClientCallback;

        private volatile WebSocketClientState _state = WebSocketClientState.None;
        private WebSocket _websocket;

        public WebSocketSharpClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback) {
            _hostUri = hostUri;
            _webSocketClientCallback = webSocketClientCallback;
        }

        public WebSocketClientState State => _state;


        public void Open() {
            SynchronizeWhen(WebSocketClientState.None, () => {
                _websocket = new WebSocket(_hostUri.ToString());
                _websocket.OnOpen += OnOpen;
                _websocket.OnClose += OnClose;
                _websocket.OnMessage += OnMessage;
                _websocket.OnError += OnError;
                _state = WebSocketClientState.Connecting;
                _websocket.ConnectAsync();
            });
        }

        public void Close() {
            SynchronizeWhenNot(WebSocketClientState.None, () => {
                _state = WebSocketClientState.Disconnecting;
                _websocket.Close(CloseStatusCode.Normal, "User invoked close");
            });
        }

        public void Send(string message) {
            SynchronizeWhen(WebSocketClientState.Connected, () => _websocket.Send(message));
        }

        // Event Handlers

        private void OnOpen(object sender, EventArgs e) {
            Synchronize(() => _state = WebSocketClientState.Connected);
            _webSocketClientCallback.OnOpen();
        }

        private void OnClose(object sender, CloseEventArgs e) {
            Synchronize(() => _state = WebSocketClientState.Disconnected);
            _webSocketClientCallback.OnClose();
        }

        private void OnMessage(object sender, MessageEventArgs e) {
            if (e.IsText) _webSocketClientCallback.OnMessage(e.Data);
            else if (e.IsBinary) {
                string hexData = BitConverter.ToString(e.RawData.SubArray(0, Math.Min(e.RawData.Length, 50))).Replace("-", "");
                _log.Warn($"Ignoring unexpected binary message > [{hexData}{(e.RawData.Length > 50 ? " ..." : "")}]");
            }
        }

        private void OnError(object sender, ErrorEventArgs e) {
            _webSocketClientCallback.OnError(e.Exception);
        }


        private void Synchronize(Action action, Func<bool> predicate = null) {
            bool stateChanged;
            lock (_mutex) {
                WebSocketClientState state = _state;
                if (predicate == null || predicate()) action();
                stateChanged = _state != state;
            }
            // notify outside lock to avoid deadlock
            if (stateChanged) _webSocketClientCallback.OnStateChanged();
        }

        private void SynchronizeWhen(WebSocketClientState state, Action action) => Synchronize(action, () => _state == state);

        private void SynchronizeWhenNot(WebSocketClientState state, Action action) => Synchronize(action, () => _state != state);

    }
}
