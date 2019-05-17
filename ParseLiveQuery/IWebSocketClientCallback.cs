using System;

namespace Parse.LiveQuery {
    public interface IWebSocketClientCallback {

        void OnOpen();

        void OnMessage(string message);

        void OnClose();

        void OnError(Exception exception);

        void OnStateChanged();

    }
}
