using System.Collections.Generic;
using Parse.Common.Internal;

namespace Parse.LiveQuery {
    public class ConnectClientOperation : SessionClientOperation {

        private readonly string _applicationId;

        internal ConnectClientOperation(string applicationId, string sessionToken) : base(sessionToken) {
            _applicationId = applicationId;
        }

        protected override IDictionary<string, object> ToJsonObject() => new Dictionary<string, object> {
            ["op"] = "connect",
            ["applicationId"] = _applicationId
        };

    }
}
