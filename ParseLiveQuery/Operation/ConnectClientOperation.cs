using System.Collections.Generic;
using Parse.Common.Internal;

namespace Parse.LiveQuery {
    public class ConnectClientOperation : IClientOperation {

        private readonly string _applicationId;
        private readonly string _sessionToken;

        internal ConnectClientOperation(string applicationId, string sessionToken) {
            _applicationId = applicationId;
            _sessionToken = sessionToken;
        }

        public string ToJson() {
            return Json.Encode(new Dictionary<string, object> {
                ["op"] = "connect",
                ["applicationId"] = _applicationId,
                ["sessionToken"] = _sessionToken
            });
        }
    }
}
