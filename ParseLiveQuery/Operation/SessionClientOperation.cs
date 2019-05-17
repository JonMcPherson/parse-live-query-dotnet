using System;
using System.Collections.Generic;
using System.Text;
using Parse.Common.Internal;

namespace Parse.LiveQuery {
    /// <summary>
    /// If you provide the sessionToken, when the LiveQuery server gets ParseObject
    /// updates from the Parse server, it will try to check whether the sessionToken fulfills
    /// the ParseObject's ACL. The LiveQuery server will only send updates to clients whose
    /// sessionToken is fit for the ParseObject's ACL.
    /// You can check the LiveQuery protocol for more details.
    /// <see href="https://github.com/parse-community/parse-server/wiki/Parse-LiveQuery-Protocol-Specification"/>
    /// </summary>
    public abstract class SessionClientOperation : IClientOperation {

        private readonly string _sessionToken;

        protected SessionClientOperation(string sessionToken) {
            _sessionToken = sessionToken;
        }

        public string ToJson() {
            IDictionary<string, object> jsonObject = ToJsonObject();
            if (_sessionToken != null) jsonObject.Add("sessionToken", _sessionToken);
            return Json.Encode(jsonObject);
        }

        protected abstract IDictionary<string, object> ToJsonObject();

    }
}
