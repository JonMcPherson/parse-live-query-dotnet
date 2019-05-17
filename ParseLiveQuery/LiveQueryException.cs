using System;

namespace Parse.LiveQuery {
    public abstract class LiveQueryException : Exception {

        private LiveQueryException() { }

        private LiveQueryException(string message) : base(message) { }

        private LiveQueryException(string message, Exception cause) : base(message, cause) { }


        /// <summary>
        /// An error that is reported when any other unknown Exception occurs unexpectedly.
        /// </summary>
        public class UnknownException : LiveQueryException {
            internal UnknownException(string message, Exception cause) : base(message, cause) { }
        }

        /// <summary>
        /// An error that is reported when the server returns a response that cannot be parsed.
        /// </summary>
        public class InvalidResponseException : LiveQueryException {
            internal InvalidResponseException(string response, Exception cause) : base(response, cause) { }
            internal InvalidResponseException(string response) : base(response) { }
        }

        /// <summary>
        /// An error that is reported when the server does not accept a query we've sent to it.
        /// </summary>
        public class InvalidQueryException : LiveQueryException {
            internal InvalidQueryException() { }
        }

        /// <summary>
        /// An error that is reported when the server returns valid JSON, but it doesn't match the format we expect.
        /// </summary>
        public class InvalidJsonException : LiveQueryException {

            internal InvalidJsonException(string json, string expectedKey) :
                base($"Invalid JSON; expectedKey {expectedKey}, json {json}") {
                Json = json;
                ExpectedKey = expectedKey;
            }

            public string Json { get; }

            public string ExpectedKey { get; }

        }

        /// <summary>
        /// An error that is reported when the live query server encounters an internal error.
        /// </summary>
        public class ServerReportedException : LiveQueryException {

            internal ServerReportedException(int code, string error, bool reconnect) :
                base($"Server reported error; code {code}, error: {error}, reconnect: {reconnect}") {
                Code = code;
                Error = error;
                IsReconnect = reconnect;
            }

            public int Code { get; }

            public string Error { get; }

            public bool IsReconnect { get; }

        }

    }
}
