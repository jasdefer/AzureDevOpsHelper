using AzureDevOpsHelper.Job.Helper;
using AzureDevOpsHelper.Job.Model;
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
    public required string Type { get; set; }
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
            string url = $"_apis/wit/workitems/${workItem.Type}?api-version=7.1";
            List<WorkItemOperation> operations =
                [
                    new WorkItemOperation("add","/fields/System.Title",workItem.Title),
                ];
            if (workItem.StartDate.HasValue)
            {
                operations.Add(new WorkItemOperation("add", "/fields/Microsoft.VSTS.Scheduling.StartDate", workItem.StartDate.Value.ToString()));
            }
            if (workItem.TargetDate.HasValue)
            {
                operations.Add(new WorkItemOperation("add", "/fields/Microsoft.VSTS.Scheduling.TargetDate", workItem.TargetDate.Value.ToString()));
            }
            var content = new StringContent(JsonSerializer.Serialize(operations), System.Text.Encoding.UTF8, "application/json-patch+json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Cannot create the work item with status code {StatusCode}: {content}", response.StatusCode, errorContent);
            }
            if (workItem.ParentId.HasValue)
            {
                if (!workItemUrlsById.TryGetValue(workItem.ParentId.Value, out string? parentUrl))
                {
                    _logger.LogWarning("The parent {ParentId} does not exist for the child {ChildTitle}", workItem.ParentId.Value, workItem.Title);
                    continue;
                }
                string createdWorkItemJson = await response.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(createdWorkItemJson);
                JsonElement root = document.RootElement;
                int workItemId = root.GetProperty("id").GetInt32();
                await _httpClient.SetParent(_logger, parentUrl, workItemId, cancellationToken);
            }
        }
    }
}
