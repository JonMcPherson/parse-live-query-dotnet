namespace Parse.LiveQuery {
    internal interface ISubscriptionFactory {

        Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query) where T : ParseObject;

    }
}
