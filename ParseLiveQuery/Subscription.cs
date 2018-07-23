using System.Collections.Generic;
using Parse.Core.Internal;

namespace Parse.LiveQuery {
    public class Subscription<T> : Subscription where T : ParseObject {

        private readonly List<IHandleEventsCallback<T>> _handleEventsCallbacks = new List<IHandleEventsCallback<T>>();
        private readonly List<IHandleErrorCallback<T>> _handleErrorCallbacks = new List<IHandleErrorCallback<T>>();
        private readonly List<IHandleSubscribeCallback<T>> _handleSubscribeCallbacks = new List<IHandleSubscribeCallback<T>>();
        private readonly List<IHandleUnsubscribeCallback<T>> _handleUnsubscribeCallbacks = new List<IHandleUnsubscribeCallback<T>>();

        internal Subscription(int requestId, ParseQuery<T> query) {
            RequestId = requestId;
            Query = query;
        }

        public int RequestId { get; }

        internal ParseQuery<T> Query { get; }

        internal override object QueryObj => Query;

        /// <summary>
        /// Register a callback for when any event occurs.
        /// </summary>
        /// <param name="callback">The events callback to register.</param>
        /// <returns>The same Subscription, for easy chaining.</returns>
        public Subscription<T> HandleEvents(IHandleEventsCallback<T> callback) {
            _handleEventsCallbacks.Add(callback);
            return this;
        }

        /// <summary>
        /// Register a callback for when a specific event occurs.
        /// </summary>
        /// <param name="subscriptionEvent">The event type to handle.</param>
        /// <param name="callback">The event callback to register.</param>
        /// <returns>The same Subscription, for easy chaining.</returns>
        public Subscription<T> HandleEvent(Event subscriptionEvent, IHandleEventCallback<T> callback) {
            return HandleEvents(new HandleEventCallbackAdapter(subscriptionEvent, callback));
        }

        /// <summary>
        /// Register a callback for when an error occurs.
        /// </summary>
        /// <param name="callback">The error callback to register.</param>
        /// <returns>The same Subscription, for easy chaining.</returns>
        public Subscription<T> HandleError(IHandleErrorCallback<T> callback) {
            _handleErrorCallbacks.Add(callback);
            return this;
        }

        /// <summary>
        /// Register a callback for when a client succesfully subscribes to a query.
        /// </summary>
        /// <param name="callback">The subscribe callback to register.</param>
        /// <returns>The same Subscription, for easy chaining.</returns>
        public Subscription<T> HandleSubscribe(IHandleSubscribeCallback<T> callback) {
            _handleSubscribeCallbacks.Add(callback);
            return this;
        }

        /// <summary>
        /// Register a callback for when a query has been unsubscribed.
        /// </summary>
        /// <param name="callback">The unsubscribe callback to register.</param>
        /// <returns>The same Subscription, for easy chaining.</returns>
        public Subscription<T> HandleUnsubscribe(IHandleUnsubscribeCallback<T> callback) {
            _handleUnsubscribeCallbacks.Add(callback);
            return this;
        }


        internal override void DidReceive(object queryObj, Event subscriptionEvent, IObjectState objState) {
            ParseQuery<T> query = (ParseQuery<T>) queryObj;
            T obj = ParseObjectExtensions.FromState<T>(objState, query.GetClassName());

            foreach (IHandleEventsCallback<T> handleEventsCallback in _handleEventsCallbacks) {
                handleEventsCallback.OnEvents(query, subscriptionEvent, obj);
            }
        }

        internal override void DidEncounter(object queryObj, LiveQueryException error) {
            foreach (IHandleErrorCallback<T> handleErrorCallback in _handleErrorCallbacks) {
                handleErrorCallback.OnError((ParseQuery<T>) queryObj, error);
            }
        }

        internal override void DidSubscribe(object queryObj) {
            foreach (IHandleSubscribeCallback<T> handleSubscribeCallback in _handleSubscribeCallbacks) {
                handleSubscribeCallback.OnSubscribe((ParseQuery<T>) queryObj);
            }
        }

        internal override void DidUnsubscribe(object queryObj) {
            foreach (IHandleUnsubscribeCallback<T> handleUnsubscribeCallback in _handleUnsubscribeCallbacks) {
                handleUnsubscribeCallback.OnUnsubscribe((ParseQuery<T>) queryObj);
            }
        }

        internal override IClientOperation CreateSubscribeClientOperation(string sessionToken) {
            return new SubscribeClientOperation<T>(this, sessionToken);
        }


        private class HandleEventCallbackAdapter : IHandleEventsCallback<T> {

            private readonly Event _subscriptionEvent;
            private readonly IHandleEventCallback<T> _callback;

            internal HandleEventCallbackAdapter(Event subscriptionEvent, IHandleEventCallback<T> callback) {
                _subscriptionEvent = subscriptionEvent;
                _callback = callback;
            }

            public void OnEvents(ParseQuery<T> query, Event callbackEvent, T obj) {
                if (callbackEvent == _subscriptionEvent) {
                    _callback.OnEvent(query, obj);
                }
            }
        }

    }

    public abstract class Subscription {

        internal abstract object QueryObj { get; }

        internal abstract void DidReceive(object queryObj, Event subscriptionEvent, IObjectState obj);

        internal abstract void DidEncounter(object queryObj, LiveQueryException error);

        internal abstract void DidSubscribe(object queryObj);

        internal abstract void DidUnsubscribe(object queryObj);

        internal abstract IClientOperation CreateSubscribeClientOperation(string sessionToken);


        public interface IHandleEventsCallback<T> where T : ParseObject {
            void OnEvents(ParseQuery<T> query, Event subscriptionEvent, T obj);
        }

        public interface IHandleEventCallback<T> where T : ParseObject {
            void OnEvent(ParseQuery<T> query, T obj);
        }

        public interface IHandleErrorCallback<T> where T : ParseObject {
            void OnError(ParseQuery<T> query, LiveQueryException exception);
        }

        public interface IHandleSubscribeCallback<T> where T : ParseObject {
            void OnSubscribe(ParseQuery<T> query);
        }

        public interface IHandleUnsubscribeCallback<T> where T : ParseObject {
            void OnUnsubscribe(ParseQuery<T> query);
        }

        public enum Event {
            Create,
            Enter,
            Update,
            Leave,
            Delete
        }

    }

}
