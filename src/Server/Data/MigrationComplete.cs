namespace CoreSyncServer.Data;

public class MigrationComplete
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _tcs.Task;

    public void Signal() => _tcs.TrySetResult();
}
