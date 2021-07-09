using System.Collections.Generic;
using Parse.Common.Internal;

namespace Parse.LiveQuery {
    public class ConnectClientOperation : SessionClientOperation {

        private readonly string _applicationId;
        private readonly string _clientKey;

        internal ConnectClientOperation(string applicationId, string clientKey, string sessionToken) : base(sessionToken) {
            _applicationId = applicationId;
            _clientKey = clientKey;
        }

        protected override IDictionary<string, object> ToJsonObject() => new Dictionary<string, object> {
            ["op"] = "connect",
            ["applicationId"] = _applicationId,
            ["clientKey"] = _clientKey
        };
    }
}
