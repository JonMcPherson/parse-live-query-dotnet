using System;

namespace Parse.LiveQuery {
    public delegate IWebSocketClient WebSocketClientFactory(Uri hostUri, IWebSocketClientCallback webSocketClientCallback);
}
