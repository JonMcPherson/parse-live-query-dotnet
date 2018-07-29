using System;
using System.Threading;
using System.Threading.Tasks;

namespace Parse.LiveQuery {
    // Interface used to abstract the concurrency of async operations in the ParseLiveQueryClient class.
    // Used by unit tests to make ParseLiveQueryClient operations execute synchronously
    internal interface ITaskQueue {

        Task Enqueue(Action taskStart);

        Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess);

        Task EnqueueOnError(Task task, Action<Exception> onError);

    }
}
