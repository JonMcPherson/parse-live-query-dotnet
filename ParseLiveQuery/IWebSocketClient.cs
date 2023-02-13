namespace Parse.LiveQuery {
    public interface IWebSocketClient {

        void Open();

        void Close();

        void Send(string message);

        WebSocketClientState State { get; }

    }
}
