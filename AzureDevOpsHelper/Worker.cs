using AzureDevOpsHelper.Job;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsHelper;
internal class Worker : IHostedService
{
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly ILogger<Worker> _logger;
    private readonly AzureDevOpsSettings _settings;
    private readonly IEnumerable<IJob> _jobs;

    public Worker(IHostApplicationLifetime hostApplicationLifetime,
        IOptions<AzureDevOpsSettings> options,
        ILogger<Worker> logger,
        IEnumerable<IJob> jobs)
    {
        _hostApplicationLifetime = hostApplicationLifetime;
        _logger = logger;
        _settings = options.Value;
        _jobs = jobs;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            string projectName = _settings.ProjectName ?? throw new ArgumentNullException(_settings.ProjectName);
            foreach (var job in _jobs)
            {
                _logger.LogDebug("Starting job {JobName}", job.GetType().Name);
                await job.Run(projectName, cancellationToken);
                _logger.LogDebug("Completed job {JobName}", job.GetType().Name);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Cannot perform Azure DevOps operations.");
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();
        }
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var job in _jobs)
        {
            job.Dispose();
        }
        return Task.CompletedTask;
    }
}
