using System;
using WebSocketSharp;

namespace Parse.LiveQuery {
    public class WebSocketSharpClientFactory : IWebSocketClientFactory {

        public IWebSocketClient CreateInstance(Uri hostUri, IWebSocketClientCallback webSocketClientCallback) {
            return new WebSocketSharpClient(hostUri, webSocketClientCallback);
        }

        private class WebSocketSharpClient : IWebSocketClient {

            private readonly object _mutex = new object();

            private readonly Uri _hostUri;
            private readonly IWebSocketClientCallback _webSocketClientCallback;

            private volatile WebSocketClientState _state = WebSocketClientState.None;
            private WebSocket _websocket;

            internal WebSocketSharpClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback) {
                _hostUri = hostUri;
                _webSocketClientCallback = webSocketClientCallback;
            }

            public WebSocketClientState State => _state;


            public void Open() {
                SynchronizedWhen(WebSocketClientState.None, () => {
                    _websocket = new WebSocket(_hostUri.ToString());
                    _websocket.OnOpen += OnOpen;
                    _websocket.OnClose += OnClose;
                    _websocket.OnMessage += OnMessage;
                    _websocket.OnError += OnError;
                    _state = WebSocketClientState.Connecting;
                });
            }

            public void Close() {
                SynchronizedWhenNot(WebSocketClientState.None, () => {
                    _state = WebSocketClientState.Disconnecting;
                    _websocket.Close(CloseStatusCode.Normal, "User invoked close");
                });
            }

            public void Send(string message) {
                SynchronizedWhen(WebSocketClientState.Connected, () => _websocket.Send(message));
            }

            // Event Handlers

            private void OnOpen(object sender, EventArgs e) {
                Synchronized(() => _state = WebSocketClientState.Connected);
                _webSocketClientCallback.OnOpen();
            }

            private void OnClose(object sender, CloseEventArgs e) {
                Synchronized(() => _state = WebSocketClientState.Disconnected);
                _webSocketClientCallback.OnClose();
            }

            private void OnMessage(object sender, MessageEventArgs e) {
                if (e.IsText) _webSocketClientCallback.OnMessage(e.Data);
                //else if (e.IsBinary) Log.w(LOG_TAG, "Socket got into inconsistent state and received %s instead.", bytes.toString()));
            }

            private void OnError(object sender, ErrorEventArgs e) {
                _webSocketClientCallback.OnError(e.Exception);
            }


            private void Synchronized(Action action, Func<bool> predicate = null) {
                bool stateChanged;
                lock (_mutex) {
                    WebSocketClientState state = _state;
                    if (predicate == null || predicate()) action();
                    stateChanged = _state != state;
                }
                // notify outside lock to avoid deadlock
                if (stateChanged) _webSocketClientCallback.OnStateChanged();
            }

            private void SynchronizedWhen(WebSocketClientState state, Action action) => Synchronized(action, () => _state == state);

            private void SynchronizedWhenNot(WebSocketClientState state, Action action) => Synchronized(action, () => _state != state);

        }

    }
}
