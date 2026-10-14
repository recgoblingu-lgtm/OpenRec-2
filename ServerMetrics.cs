namespace OpenRec_2;

public sealed class ServerMetrics
{
    private long _requests;
    private long _errors;
    private long _activeRequests;
    private long _startedAtTicks = DateTimeOffset.UtcNow.UtcTicks;

    public long Requests => Interlocked.Read(ref _requests);
    public long Errors => Interlocked.Read(ref _errors);
    public long ActiveRequests => Interlocked.Read(ref _activeRequests);
    public DateTimeOffset StartedAt => new(Interlocked.Read(ref _startedAtTicks), TimeSpan.Zero);

    public RequestScope BeginRequest()
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Increment(ref _activeRequests);
        return new RequestScope(this);
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _errors);
    }

    public readonly struct RequestScope : IDisposable
    {
        private readonly ServerMetrics _metrics;

        public RequestScope(ServerMetrics metrics)
        {
            _metrics = metrics;
        }

        public void Dispose()
        {
            Interlocked.Decrement(ref _metrics._activeRequests);
        }
    }
}
