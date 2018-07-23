using System;

namespace Parse.LiveQuery {
    public interface IWebSocketClientFactory {

        IWebSocketClient CreateInstance(Uri hostUri, IWebSocketClientCallback webSocketClientCallback);

    }
}
