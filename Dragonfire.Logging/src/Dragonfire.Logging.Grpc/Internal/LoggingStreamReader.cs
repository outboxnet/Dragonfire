using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Dragonfire.Logging.Grpc.Internal
{
    /// <summary>
    /// Wraps an <see cref="IAsyncStreamReader{T}"/> and fires callbacks when the stream
    /// completes normally (<paramref name="onComplete"/>) or faults (<paramref name="onError"/>).
    ///
    /// Used by both server and client interceptors for streaming call types where there is
    /// no single response object to await — the call is considered complete when
    /// <see cref="MoveNext"/> returns <c>false</c>.
    /// </summary>
    internal sealed class LoggingStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly IAsyncStreamReader<T> _inner;
        private readonly Action _onComplete;
        private readonly Action<Exception> _onError;
        private bool _completed;

        internal LoggingStreamReader(
            IAsyncStreamReader<T> inner,
            Action onComplete,
            Action<Exception> onError)
        {
            _inner      = inner;
            _onComplete = onComplete;
            _onError    = onError;
        }

        public T Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            try
            {
                var hasNext = await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
                if (!hasNext && !_completed)
                {
                    _completed = true;
                    _onComplete();
                }
                return hasNext;
            }
            catch (Exception ex) when (!_completed)
            {
                _completed = true;
                _onError(ex);
                throw;
            }
        }
    }
}
