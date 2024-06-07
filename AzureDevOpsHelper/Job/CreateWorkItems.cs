using AzureDevOpsHelper.Job.Wiql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AzureDevOpsHelper.Job;

public class WorkItemSettings
{
    public List<WorkItem> WorkItems { get; set; } = [];
}

public class WorkItem
{
    public required string Title { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? TargetDate { get; set; }
    public int? ParentId { get; set; }
}
internal class CreateWorkItems : IJob
{
    private readonly ILogger<CreateWorkItems> _logger;
    private readonly HttpClient _httpClient;
    private readonly List<WorkItem> _workItems;

    public CreateWorkItems(ILogger<CreateWorkItems> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<WorkItemSettings> options)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("AzureDevOps");
        _workItems = options.Value.WorkItems;
    }
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task Run(string project, CancellationToken cancellationToken)
    {
        string query = $"Select [System.Id] From WorkItems Where [System.TeamProject] = '{project}'";
        string json = await _httpClient.Query(query, cancellationToken, _logger);
        WorkItemResponse workItemResponse = JsonSerializer.Deserialize<WorkItemResponse>(json)
            ?? throw new InvalidDataException($"Cannot parse json {json}");
        Dictionary<int, string> workItemUrlsById = workItemResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);
        foreach (var workItem in _workItems)
        {

        }
        throw new NotImplementedException();
    }
}
