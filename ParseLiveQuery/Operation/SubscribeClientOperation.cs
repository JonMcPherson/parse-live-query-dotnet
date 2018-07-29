using System.Collections.Generic;
using Parse.Common.Internal;
using Parse.Core.Internal;

namespace Parse.LiveQuery {
    public class SubscribeClientOperation<T> : IClientOperation where T : ParseObject {

        private readonly int _requestId;
        private readonly ParseQuery<T> _query;
        private readonly string _sessionToken;

        internal SubscribeClientOperation(Subscription<T> subscription, string sessionToken) {
            _requestId = subscription.RequestId;
            _query = subscription.Query;
            _sessionToken = sessionToken;
        }

        // TODO: add support for fields
        // https://github.com/ParsePlatform/parse-server/issues/3671
        public string ToJson() {
            return Json.Encode(new Dictionary<string, object> {
                ["op"] = "subscribe",
                ["requestId"] = _requestId,
                ["sessionToken"] = _sessionToken,
                ["query"] = new Dictionary<string, object> {
                    ["className"] = _query.GetClassName(),
                    ["where"] = _query.BuildParameters().TryGetValue("where", out object where) ? where : null
                }
            });
        }

    }
}
