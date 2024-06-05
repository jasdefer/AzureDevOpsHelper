namespace AzureDevOpsHelper.Job;
internal interface IJob : IDisposable
{
    public Task Run(string project, CancellationToken cancellationToken);
}
