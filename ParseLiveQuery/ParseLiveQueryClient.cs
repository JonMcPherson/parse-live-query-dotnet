using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Parse.Common.Internal;
using Parse.Core.Internal;

namespace Parse.LiveQuery {
    public class ParseLiveQueryClient {

        private readonly Uri _hostUri;
        private readonly string _applicationId;
        private readonly IWebSocketClientFactory _webSocketClientFactory;
        private readonly IWebSocketClientCallback _webSocketClientCallback;

        private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new ConcurrentDictionary<int, Subscription>();
        private readonly List<IParseLiveQueryClientCallbacks> _callbacks = new List<IParseLiveQueryClientCallbacks>();
        private readonly TaskQueue _taskQueue = new TaskQueue();

        private IWebSocketClient _webSocketClient;
        private int _requestIdCount = 1;
        private bool _userInitiatedDisconnect;
        private bool _hasReceivedConnected;

        public ParseLiveQueryClient() : this(GetDefaultUri()) { }

        public ParseLiveQueryClient(Uri hostUri) : this(hostUri, new WebSocketSharpClientFactory()) { }

        public ParseLiveQueryClient(IWebSocketClientFactory webSocketClientFactory) : this(GetDefaultUri(), webSocketClientFactory) { }

        public ParseLiveQueryClient(Uri hostUri, IWebSocketClientFactory webSocketClientFactory) {
            _hostUri = hostUri;
            _applicationId = ParseClient.CurrentConfiguration.ApplicationId;
            _webSocketClientFactory = webSocketClientFactory;
            _webSocketClientCallback = new WebSocketClientCallback(this);
        }

        private static Uri GetDefaultUri() {
            string server = ParseClient.CurrentConfiguration.Server;
            if (server == null) throw new InvalidOperationException("Missing default Server URI in CurrentConfiguration");

            Uri serverUri = new Uri(server);
            return new UriBuilder(serverUri) {
                Scheme = serverUri.Scheme.Equals("https") ? "wss" : "ws"
            }.Uri;
        }


        public Subscription<T> Subscribe<T>(ParseQuery<T> query) where T : ParseObject {
            int requestId = _requestIdCount++;
            Subscription<T> subscription = new Subscription<T>(requestId, query);
            _subscriptions.TryAdd(requestId, subscription);

            if (IsConnected()) {
                SendSubscription(subscription);
            } else if (_userInitiatedDisconnect) {
                throw new InvalidOperationException("The client was explicitly disconnected and must be reconnected before subscribing");
            } else {
                ConnectIfNeeded();
            }
            return subscription;
        }

        public void ConnectIfNeeded() {
            switch (GetWebSocketState()) {
                case WebSocketClientState.None:
                case WebSocketClientState.Disconnecting:
                case WebSocketClientState.Disconnected:
                    Reconnect();
                    break;
            }
        }

        public void Unsubscribe<T>(ParseQuery<T> query) where T : ParseObject {
            if (query == null) return;
            foreach (Subscription sub in _subscriptions.Values) {
                if (query.Equals(sub.QueryObj)) {
                    SendUnsubscription((Subscription<T>) sub);
                }
            }
        }

        public void Unsubscribe<T>(ParseQuery<T> query, Subscription<T> subscription) where T : ParseObject {
            if (query == null || subscription == null) return;
            foreach (Subscription sub in _subscriptions.Values) {
                if (query.Equals(sub.QueryObj) && subscription.Equals(sub)) {
                    SendUnsubscription(subscription);
                }
            }
        }

        public void Reconnect() {
            _webSocketClient?.Close();

            _userInitiatedDisconnect = false;
            _hasReceivedConnected = false;
            _webSocketClient = _webSocketClientFactory.CreateInstance(_hostUri, _webSocketClientCallback);
            _webSocketClient.Open();
        }

        public void Disconnect() {
            _webSocketClient?.Close();
            _webSocketClient = null;

            _userInitiatedDisconnect = true;
            _hasReceivedConnected = false;
        }

        public void RegisterListener(IParseLiveQueryClientCallbacks listener) {
            _callbacks.Add(listener);
        }

        public void UnregisterListener(IParseLiveQueryClientCallbacks listener) {
            _callbacks.Add(listener);
        }

        // Private methods


        private WebSocketClientState GetWebSocketState() {
            return _webSocketClient?.State ?? WebSocketClientState.None;
        }

        private bool IsConnected() {
            return _hasReceivedConnected && GetWebSocketState() == WebSocketClientState.Connected;
        }


        private void SendSubscription(Subscription subscription) {
            SendOperationWithSessionAsync(subscription.CreateSubscribeClientOperation).ContinueWith(task => {
                if (task.Exception != null) {
                    subscription.DidEncounter(subscription.QueryObj,
                        new LiveQueryException.UnknownException("Error when subscribing", task.Exception));
                }
            });
        }

        private void SendUnsubscription<T>(Subscription<T> subscription) where T : ParseObject {
            SendOperationAsync(new UnsubscribeClientOperation(subscription.RequestId));
        }

        private Task SendOperationWithSessionAsync(Func<string, IClientOperation> operationFunc) {
            return ParseSession.GetCurrentSessionAsync().OnSuccess(task => SendOperationAsync(operationFunc(task.Result.SessionToken)));
        }

        private Task SendOperationAsync(IClientOperation operation) {
            return _taskQueue.Enqueue(task => task.ContinueWith(_ => _webSocketClient.Send(operation.ToJson())), CancellationToken.None);
        }

        private Task HandleOperationAsync(string message) {
            return _taskQueue.Enqueue(task => task.ContinueWith(_ => ParseMessage(message)), CancellationToken.None);
        }


        private void ParseMessage(string message) {
            try {
                IDictionary<string, object> jsonObject = (IDictionary<string, object>) Json.Parse(message);
                string rawOperation = (string) jsonObject["op"];

                switch (rawOperation) {
                    case "connected":
                        _hasReceivedConnected = true;
                        DispatchConnected();
                        foreach (Subscription subscription in _subscriptions.Values) {
                            SendSubscription(subscription);
                        }
                        break;
                    case "redirect":
                        // TODO: Handle redirect.
                        //string url = (string) jsonObject["url"];
                        break;
                    case "subscribed":
                        HandleSubscribedEvent(jsonObject);
                        break;
                    case "unsubscribed":
                        HandleUnsubscribedEvent(jsonObject);
                        break;
                    case "enter":
                        HandleObjectEvent(Subscription.Event.Enter, jsonObject);
                        break;
                    case "leave":
                        HandleObjectEvent(Subscription.Event.Leave, jsonObject);
                        break;
                    case "update":
                        HandleObjectEvent(Subscription.Event.Update, jsonObject);
                        break;
                    case "create":
                        HandleObjectEvent(Subscription.Event.Create, jsonObject);
                        break;
                    case "delete":
                        HandleObjectEvent(Subscription.Event.Delete, jsonObject);
                        break;
                    case "error":
                        HandleErrorEvent(jsonObject);
                        break;
                    default:
                        throw new LiveQueryException.InvalidResponseException(message);
                }
            } catch (Exception) {
                throw new LiveQueryException.InvalidResponseException(message);
            }
        }

        private void DispatchConnected() {
            foreach (IParseLiveQueryClientCallbacks callback in _callbacks) {
                callback.OnLiveQueryClientConnected(this);
            }
        }

        private void DispatchDisconnected() {
            foreach (IParseLiveQueryClientCallbacks callback in _callbacks) {
                callback.OnLiveQueryClientDisconnected(this, _userInitiatedDisconnect);
            }
        }

        private void DispatchError(LiveQueryException exception) {
            foreach (IParseLiveQueryClientCallbacks callback in _callbacks) {
                callback.OnLiveQueryError(this, exception);
            }
        }

        private void DispatchSocketError(Exception exception) {
            _userInitiatedDisconnect = false;

            foreach (IParseLiveQueryClientCallbacks callback in _callbacks) {
                callback.OnSocketError(this, exception);
            }

            DispatchDisconnected();
        }


        private void HandleSubscribedEvent(IDictionary<string, object> jsonObject) {
            int requestId = (int) jsonObject["requestId"];

            Subscription subscription = _subscriptions[requestId];
            subscription?.DidSubscribe(subscription.QueryObj);
        }

        private void HandleUnsubscribedEvent(IDictionary<string, object> jsonObject) {
            int requestId = (int) jsonObject["requestId"];

            if (_subscriptions.TryRemove(requestId, out Subscription subscription)) {
                subscription.DidUnsubscribe(subscription.QueryObj);
            }
        }

        private void HandleObjectEvent(Subscription.Event subscriptionEvent, IDictionary<string, object> jsonObject) {
            int requestId = (int) jsonObject["requestId"];

            Subscription subscription = _subscriptions[requestId];
            if (subscription != null) {
                IObjectState objState = ParseObjectCoder.Instance.Decode(jsonObject, ParseDecoder.Instance);
                subscription.DidReceive(subscription.QueryObj, subscriptionEvent, objState);
            }
        }

        private void HandleErrorEvent(IDictionary<string, object> jsonObject) {
            int requestId = (int) jsonObject["requestId"];
            int code = (int) jsonObject["code"];
            string error = (string) jsonObject["error"];
            bool reconnect = (bool) jsonObject["reconnect"];

            Subscription subscription = _subscriptions[requestId];
            LiveQueryException exception = new LiveQueryException.ServerReportedException(code, error, reconnect);
            subscription?.DidEncounter(subscription.QueryObj, exception);
            DispatchError(exception);
        }


        private class WebSocketClientCallback : IWebSocketClientCallback {

            private readonly ParseLiveQueryClient _client;

            public WebSocketClientCallback(ParseLiveQueryClient client) {
                _client = client;
            }

            public void OnOpen() {
                _client._hasReceivedConnected = false;
                _client.SendOperationWithSessionAsync(session => new ConnectClientOperation(_client._applicationId, session)).ContinueWith(task => {
                    if (task.Exception != null) {
                        _client.DispatchError(task.Exception.InnerException as LiveQueryException ??
                            new LiveQueryException.UnknownException("Error connecting client", task.Exception));
                    }
                });
            }

            public void OnMessage(string message) {
                _client.HandleOperationAsync(message).ContinueWith(task => {
                    if (task.Exception != null) {
                        _client.DispatchError(task.Exception.InnerException as LiveQueryException ??
                            new LiveQueryException.UnknownException("Error handling message " + message, task.Exception));
                    }
                });
            }

            public void OnClose() {
                _client._hasReceivedConnected = false;
                _client.DispatchDisconnected();
            }

            public void OnError(Exception exception) {
                _client._hasReceivedConnected = false;
                _client.DispatchSocketError(exception);
            }

            public void OnStateChanged() {
                // do nothing or maybe TODO logging
            }

        }

    }
}
