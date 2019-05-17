using System;

namespace Parse.LiveQuery {
    public interface IParseLiveQueryClientCallbacks {

        void OnLiveQueryClientConnected(ParseLiveQueryClient client);

        void OnLiveQueryClientDisconnected(ParseLiveQueryClient client, bool userInitiated);

        void OnLiveQueryError(ParseLiveQueryClient client, LiveQueryException reason);

        void OnSocketError(ParseLiveQueryClient client, Exception reason);

    }
}
