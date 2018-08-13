using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NSubstitute;
using Parse.Common.Internal;
using Parse.Core.Internal;
using static Parse.LiveQuery.LiveQueryException;
using static Parse.LiveQuery.Subscription;

namespace Parse.LiveQuery.Test {
    public abstract class ParseLiveQueryClientTest {

        private TestSubscriptionFactory _subscriptionFactory;
        private IParseCurrentUserController _currentUserController;

        private ParseLiveQueryClient _parseLiveQueryClient;
        private IWebSocketClient _webSocketClient;
        private IWebSocketClientCallback _webSocketClientCallback;


        [SetUp]
        public void SetUp() {
            ParseObject.RegisterSubclass<ParseUser>();
            ParseUser mockUser = ParseObject.Create<ParseUser>();
            _subscriptionFactory = new TestSubscriptionFactory();

            _currentUserController = Substitute.For<IParseCurrentUserController>();
            _currentUserController.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(mockUser));
            _currentUserController.GetCurrentSessionTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(mockUser.SessionToken));

            ParseCorePlugins.Instance = new ParseCorePlugins { CurrentUserController = _currentUserController };

            _parseLiveQueryClient = new ParseLiveQueryClient(new Uri("/", UriKind.Relative), (hostUri, callback) => {
                _webSocketClientCallback = callback;
                _webSocketClient = Substitute.For<IWebSocketClient>();
                return _webSocketClient;
            }, _subscriptionFactory, new ImmediateTaskQueue());

            Reconnect();
        }

        [TearDown]
        public void TearDown() => ParseCorePlugins.Instance.Reset();


        public class SubscribeMethod : ParseLiveQueryClientTest {
            [Test]
            public void ShouldConnectIfNeeded() {
                // Reset connection
                _webSocketClient = null;
                _webSocketClientCallback = null;

                // This should trigger ConnectIfNeeded(), which calls reconnect()
                _parseLiveQueryClient.Subscribe(new ParseQuery<ParseObject>());

                Assert.NotNull(_webSocketClient);
                Assert.NotNull(_webSocketClientCallback);
                _webSocketClient.Received().Open();
                _webSocketClient.DidNotReceive().Send(Arg.Any<string>());

                // Now the socket is open
                _webSocketClientCallback.OnOpen();
                _webSocketClient.State.Returns(WebSocketClientState.Connected);

                // and we send op=connect
                _webSocketClient.Received().Send(Arg.Is<string>(msg => msg.Contains("\"op\":\"connect\"")));

                // Now if we subscribe to queryB, we SHOULD NOT send the subscribe yet, until we get op=connected
                _parseLiveQueryClient.Subscribe(new ParseQuery<ParseObject>());

                _webSocketClient.DidNotReceive().Send(Arg.Is<string>(msg => msg.Contains("\"op\":\"subscribe\"")));

                // on op=connected, _then_ we should send both subscriptions
                _webSocketClientCallback.OnMessage(MockConnectedMessage());

                _webSocketClient.Received(2).Send(Arg.Is<string>(msg => msg.Contains("\"op\":\"subscribe\"")));
            }

            [Test]
            public void ShouldNotifySubscribedCallbacks() {
                SubscribeCallback<ParseObject> subscribeCallback = Substitute.For<SubscribeCallback<ParseObject>>();

                ParseQuery<ParseObject> query = new ParseQuery<ParseObject>("test");
                Subscribe(query, subscribeCallback);

                subscribeCallback.Received().Invoke(query);
            }

            [Test]
            public void ShouldHandleInternalErrorsAndNotifyCallbacks() {
                IClientOperation errorSubscribeOperation = Substitute.For<IClientOperation>();
                errorSubscribeOperation.ToJson().Returns(x => throw new Exception("forced error"));

                ParseQuery<ParseObject> query = new ParseQuery<ParseObject>();
                _subscriptionFactory.CreateSubscriptionFunction = () => new TestSubscription(1, query) {
                    CreateSubscribeClientOperationFunction = _ => errorSubscribeOperation
                };

                LiveQueryException exception = null;
                ErrorCallback<ParseObject> errorCallback = Substitute.For<ErrorCallback<ParseObject>>();
                errorCallback.Invoke(query, Arg.Do<LiveQueryException>(e => exception = e));


                _parseLiveQueryClient.Subscribe(query).HandleError(errorCallback);
                _webSocketClientCallback.OnMessage(MockConnectedMessage()); // Trigger a re-subscribe


                // This will never get a chance to call op=subscribe, because an exception was thrown
                _webSocketClient.DidNotReceive().Send(Arg.Any<string>());
                errorCallback.Received().Invoke(query, exception);
                Assert.AreEqual("Error when subscribing", exception.Message);
                Assert.NotNull(exception.GetBaseException());
            }

            [Test]
            public void ShouldHandleServerErrorsAndNotifyCallbacks() {
                LiveQueryException exception = null;
                ParseQuery<ParseObject> query = new ParseQuery<ParseObject>("test");
                ErrorCallback<ParseObject> errorCallback = Substitute.For<ErrorCallback<ParseObject>>();
                errorCallback.Invoke(query, Arg.Do<LiveQueryException>(e => exception = e));


                Subscription<ParseObject> subscription = Subscribe(query).HandleError(errorCallback);
                _webSocketClientCallback.OnMessage(MockErrorMessage(subscription.RequestId));


                errorCallback.Received().Invoke(query, exception);
                Assert.IsInstanceOf<ServerReportedException>(exception);

                ServerReportedException serverException = (ServerReportedException) exception;
                Assert.AreEqual("testError", serverException.Error);
                Assert.AreEqual(1, serverException.Code);
                Assert.AreEqual(true, serverException.IsReconnect);
            }

            [Test]
            public void ShouldWorkWithHeterogeneousSubscriptions() {
                ParseObject.RegisterSubclass<MockClassA>();
                ParseObject.RegisterSubclass<MockClassB>();

                ErrorCallback<MockClassA> errorCallback1 = Substitute.For<ErrorCallback<MockClassA>>();
                errorCallback1.When(x => x.Invoke(Arg.Any<ParseQuery<MockClassA>>(), Arg.Any<LiveQueryException>()))
                    .Throw(c => c.ArgAt<LiveQueryException>(1));
                ErrorCallback<MockClassB> errorCallback2 = Substitute.For<ErrorCallback<MockClassB>>();
                errorCallback2.When(x => x.Invoke(Arg.Any<ParseQuery<MockClassB>>(), Arg.Any<LiveQueryException>()))
                    .Throw(c => c.ArgAt<LiveQueryException>(1));

                ValidateReceivedEqualObjects(Event.Create, errorCallback1);
                ValidateReceivedEqualObjects(Event.Create, errorCallback2);
            }
        }

        public class UnsubscribeMethod : ParseLiveQueryClientTest {
            [Test]
            public void ShouldNotifySubscribedCallbacks() {
                ParseQuery<ParseObject> parseQuery = new ParseQuery<ParseObject>("test");
                UnsubscribeCallback<ParseObject> unsubscribeCallback = Substitute.For<UnsubscribeCallback<ParseObject>>();

                Subscription<ParseObject> subscription = Subscribe(parseQuery).HandleUnsubscribe(unsubscribeCallback);
                _parseLiveQueryClient.Unsubscribe(parseQuery);

                _webSocketClient.Received().Send(Arg.Any<string>());
                unsubscribeCallback.DidNotReceive().Invoke(Arg.Any<ParseQuery<ParseObject>>());

                _webSocketClientCallback.OnMessage(MockUnsubscribedMessage(subscription.RequestId));

                unsubscribeCallback.Received().Invoke(parseQuery);
            }

            [Test]
            public void ShouldCloseSubscriptions() {
                ParseQuery<ParseObject> query = new ParseQuery<ParseObject>("test");
                EventCallback<ParseObject> eventCallback = Substitute.For<EventCallback<ParseObject>>();
                UnsubscribeCallback<ParseObject> unsubscribeCallback = Substitute.For<UnsubscribeCallback<ParseObject>>();

                Subscription<ParseObject> subscription = Subscribe(query)
                    .HandleEvent(Event.Create, eventCallback)
                    .HandleUnsubscribe(unsubscribeCallback);
                _parseLiveQueryClient.Unsubscribe(query);

                _webSocketClient.Received().Send(Arg.Any<string>());

                _webSocketClientCallback.OnMessage(MockUnsubscribedMessage(subscription.RequestId));

                unsubscribeCallback.Received().Invoke(query);

                ParseObject parseObject = new ParseObject("Test") { ObjectId = "testId" };
                _webSocketClientCallback.OnMessage(MockObjectEventMessage(Event.Create, subscription.RequestId, parseObject));

                eventCallback.DidNotReceive().Invoke(query, Arg.Any<ParseObject>());
            }
        }


        public class WebSocketClientHandler : ParseLiveQueryClientTest {
            [Test]
            public void ShouldParseObjectEventsAndNotifySubscribedCallbacks() {
                foreach (Event objEvent in (Event[]) Enum.GetValues(typeof(Event))) {
                    ValidateReceivedEqualObjects(objEvent);
                }
            }

            [Test]
            public void ShouldResendSubscriptionsWhenReconnected() {
                Subscribe(new ParseQuery<ParseObject>("testA"));
                Subscribe(new ParseQuery<ParseObject>("testB"));

                _parseLiveQueryClient.Disconnect();
                Reconnect();

                _webSocketClient.Received(3).Send(Arg.Any<string>());
            }

            [Test]
            public void ShouldSendSessionTokenOnConnect() {
                _currentUserController.GetCurrentSessionTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("T0k3n"));

                _parseLiveQueryClient.Reconnect();
                _webSocketClientCallback.OnOpen();

                _webSocketClient.Received().Send(Arg.Is<string>(msg => msg.Contains("\"sessionToken\":\"T0k3n\"")));
            }

            [Test]
            public void ShouldSendSessionTokenOnSubscribe() {
                _currentUserController.GetCurrentSessionTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("T0k3n"));
                _webSocketClient.State.Returns(WebSocketClientState.Connected);

                _parseLiveQueryClient.Subscribe(new ParseQuery<ParseObject>("Test"));

                _webSocketClient.Received().Send(Arg.Is<string>(msg => msg.Contains("\"op\":\"connect\"")));
                _webSocketClient.Received().Send(Arg.Is<string>(msg =>
                    msg.Contains("\"op\":\"subscribe\"") && msg.Contains("\"sessionToken\":\"T0k3n\"")
                ));
            }

            [Test]
            public void ShouldNotifyClientCallbacksOnUnexpectedDisconnect() {
                IParseLiveQueryClientCallbacks callbacks = RegisterCallbacks();

                // Unexpected close from the server:
                _webSocketClientCallback.OnClose();

                callbacks.Received().OnLiveQueryClientDisconnected(_parseLiveQueryClient, userInitiated: false);
            }

            [Test]
            public void ShouldNotifyClientCallbacksOnExpectedDisconnect() {
                IParseLiveQueryClientCallbacks callbacks = RegisterCallbacks();

                _parseLiveQueryClient.Disconnect();

                _webSocketClient.Received().Close();

                Assert.IsEmpty(callbacks.ReceivedCalls());

                // the client is a mock, so it won't actually invoke the callback automatically
                _webSocketClientCallback.OnClose();

                callbacks.Received().OnLiveQueryClientDisconnected(_parseLiveQueryClient, userInitiated: true);
            }

            [Test]
            public void ShouldNotifyClientCallbacksOnConnect() {
                IParseLiveQueryClientCallbacks callbacks = RegisterCallbacks();

                Reconnect();

                callbacks.Received().OnLiveQueryClientConnected(_parseLiveQueryClient);
            }

            [Test]
            public void ShouldNotifyClientCallbacksOnSocketError() {
                IParseLiveQueryClientCallbacks callbacks = RegisterCallbacks();

                _webSocketClientCallback.OnError(new Exception("bad things happened"));

                callbacks.Received().OnSocketError(_parseLiveQueryClient, Arg.Is<Exception>(e => e.Message.Equals("bad things happened")));
                callbacks.Received().OnLiveQueryClientDisconnected(_parseLiveQueryClient, userInitiated: false);
            }

            [Test]
            public void ShouldNotifyClientCallbacksOnError() {
                IParseLiveQueryClientCallbacks callbacks = RegisterCallbacks();

                _webSocketClientCallback.OnMessage(MockErrorMessage(1));

                callbacks.Received().OnLiveQueryError(_parseLiveQueryClient,
                    Arg.Is<ServerReportedException>(e => e.Code == 1 && e.Error == "testError" && e.IsReconnect));
            }

            private IParseLiveQueryClientCallbacks RegisterCallbacks() {
                IParseLiveQueryClientCallbacks callbacks = Substitute.For<IParseLiveQueryClientCallbacks>();

                _parseLiveQueryClient.RegisterListener(callbacks);

                Assert.IsEmpty(callbacks.ReceivedCalls());
                return callbacks;
            }
        }


        private void ValidateReceivedEqualObjects(Event objEvent, ErrorCallback<ParseObject> errorCallback = null) =>
            ValidateReceivedEqualObjects(objEvent, () => new ParseObject("test"), errorCallback);

        private void ValidateReceivedEqualObjects<T>(Event objEvent, ErrorCallback<T> errorCallback = null) where T : ParseObject, new() =>
            ValidateReceivedEqualObjects(objEvent, () => new T(), errorCallback);

        private void ValidateReceivedEqualObjects<T>(Event objEvent, Func<T> objInitFunction, ErrorCallback<T> errorCallback = null)
            where T : ParseObject {
            T expectedObj = objInitFunction();
            expectedObj.ObjectId = "testId";
            ParseQuery<T> query = new ParseQuery<T>(expectedObj.ClassName);

            T actualObj = null;
            EventCallback<T> eventCallback = Substitute.For<EventCallback<T>>();
            EventsCallback<T> eventsCallback = Substitute.For<EventsCallback<T>>();
            eventCallback.Invoke(query, Arg.Do<T>(obj => actualObj = obj));

            Subscription<T> subscription = Subscribe(query).HandleEvent(objEvent, eventCallback).HandleEvents(eventsCallback);
            if (errorCallback != null) subscription.HandleError(errorCallback);
            _webSocketClientCallback.OnMessage(MockObjectEventMessage(objEvent, subscription.RequestId, expectedObj));

            eventCallback.Received().Invoke(query, actualObj);
            eventsCallback.Received().Invoke(query, objEvent, actualObj);
            Assert.AreEqual(expectedObj.ClassName, actualObj.ClassName);
            Assert.AreEqual(expectedObj.ObjectId, actualObj.ObjectId);
        }


        private Subscription<T> Subscribe<T>(ParseQuery<T> parseQuery, SubscribeCallback<T> subscribeCallback = null) where T : ParseObject {
            Subscription<T> subscription = _parseLiveQueryClient.Subscribe(parseQuery);
            if (subscribeCallback != null) subscription.HandleSubscribe(subscribeCallback);
            _webSocketClientCallback.OnMessage(MockSubscribedMessage(subscription.RequestId));
            return subscription;
        }

        private void Reconnect() {
            _parseLiveQueryClient.Reconnect();
            _webSocketClientCallback.OnOpen();
            _webSocketClientCallback.OnMessage(MockConnectedMessage());
        }


        private static string MockConnectedMessage() => Json.Encode(new Dictionary<string, object> {
            ["op"] = "connected"
        });

        private static string MockSubscribedMessage(int requestId) => Json.Encode(new Dictionary<string, object> {
            ["op"] = "subscribed",
            ["clientId"] = 1,
            ["requestId"] = requestId
        });

        private static string MockUnsubscribedMessage(int requestId) => Json.Encode(new Dictionary<string, object> {
            ["op"] = "unsubscribed",
            ["requestId"] = requestId
        });

        private static string MockErrorMessage(int requestId) => Json.Encode(new Dictionary<string, object> {
            ["op"] = "error",
            ["requestId"] = requestId,
            ["code"] = 1,
            ["error"] = "testError",
            ["reconnect"] = true
        });

        private static string MockObjectEventMessage(Event objEvent, int requestId, ParseObject obj) => Json.Encode(new Dictionary<string, object> {
            ["op"] = objEvent.ToString().ToLower(),
            ["requestId"] = requestId,
            ["object"] = PointerOrLocalIdEncoder.Instance.Encode(obj)
        });


        [ParseClassName("MockA")]
        public class MockClassA : ParseObject { }

        [ParseClassName("MockB")]
        public class MockClassB : ParseObject { }


        // Override rather than mock because Subscription class is concrete with internal behavior that isn't exposed through an interface
        private class TestSubscription : Subscription<ParseObject> {
            internal Func<string, IClientOperation> CreateSubscribeClientOperationFunction { private get; set; }

            internal TestSubscription(int requestId, ParseQuery<ParseObject> query) : base(requestId, query) { }

            internal override IClientOperation CreateSubscribeClientOperation(string sessionToken) {
                if (CreateSubscribeClientOperationFunction == null) return base.CreateSubscribeClientOperation(sessionToken);
                return CreateSubscribeClientOperationFunction(sessionToken);
            }
        }

        private class TestSubscriptionFactory : ISubscriptionFactory {
            internal Func<object> CreateSubscriptionFunction { private get; set; }

            public Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query) where T : ParseObject {
                if (CreateSubscriptionFunction == null) return new Subscription<T>(requestId, query);
                return (Subscription<T>) CreateSubscriptionFunction();
            }
        }

        private class ImmediateTaskQueue : ITaskQueue {
            public Task Enqueue(Action taskStart) {
                try {
                    taskStart();
                    return Task.CompletedTask;
                } catch (Exception e) {
                    return Task.FromException(e);
                }
            }

            public Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess) {
                if (!task.IsFaulted && !task.IsCanceled) return onSuccess(task);
                return task;
            }

            public Task EnqueueOnError(Task task, Action<Exception> onError) {
                if (task.Exception != null) onError(task.Exception);
                return task;
            }
        }

    }
}
