using System.Collections.Generic;
using Parse.Common.Internal;

namespace Parse.LiveQuery {
    public class UnsubscribeClientOperation : IClientOperation {

        private readonly int _requestId;

        internal UnsubscribeClientOperation(int requestId) {
            _requestId = requestId;
        }

        public string ToJson() {
            return Json.Encode(new Dictionary<string, object> {
                ["op"] = "unsubscribe",
                ["requestId"] = _requestId
            });
        }
    }
}
