using AzureDevOpsHelper.Job.Helper;
using AzureDevOpsHelper.Job.Model;
using AzureDevOpsHelper.Job.Wiql;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AzureDevOpsHelper.Job;
internal record WorkItemDetails(DateTime? StartDate, DateTime? TargetDate, int? ParentId);

internal class SetParentDates : IJob
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SetParentDates> _logger;

    public SetParentDates(IHttpClientFactory httpClientFactory,
        ILogger<SetParentDates> logger)
    {
        _httpClient = httpClientFactory.CreateClient("AzureDevOps");
        _logger = logger;
    }
    public async Task Run(string project, CancellationToken cancellationToken)
    {
        await Run(project, "Product Backlog Item", cancellationToken);
        await Run(project, "Feature", cancellationToken);
    }

    public async Task Run(string project, string workItemType, CancellationToken cancellationToken)
    {
        string query = $"Select [System.Id], [System.WorkItemType], [Start Date], [Target Date], [System.Parent] From WorkItems Where [System.TeamProject] = '{project}' AND [System.WorkItemType] = '{workItemType}'";
        string json = await _httpClient.Query(query, cancellationToken, _logger);
        WorkItemResponse workItemResponse = JsonSerializer.Deserialize<WorkItemResponse>(json)
            ?? throw new InvalidDataException($"Cannot parse json {json}");
        ConcurrentDictionary<int, DateTime> startDates = [];
        ConcurrentDictionary<int, DateTime> targetDates = [];
        _logger.LogInformation("Received {Count} work items of the type {WorkItemType}", workItemResponse.WorkItemNavigations.Length, workItemType);
        foreach (var workItem in workItemResponse.WorkItemNavigations)
        {
            WorkItemDetails? details = await GetProductBacklogItemDetails(workItem.Id, cancellationToken);
            if (details is not null)
            {
                UpdateDictionaries(startDates, targetDates, details);
            }
        }

        _logger.LogInformation("Computed {StartDateCount} start dates, and {TargetDateCount} target dates for work items", startDates.Count, targetDates.Count);

        int counter = 0;
        foreach (var workItemId in startDates.Keys.Union(targetDates.Keys))
        {
            counter++;
            await UpdateParents(startDates, targetDates, workItemId, cancellationToken);
        }
        _logger.LogInformation("Updated {Count} work items", counter);
    }

    private async Task UpdateParents(ConcurrentDictionary<int, DateTime> startDates, ConcurrentDictionary<int, DateTime> targetDates, int workItemId, CancellationToken cancellationToken)
    {
        List<WorkItemOperation> patchOperations = [];
        if (startDates.TryGetValue(workItemId, out DateTime startDate))
        {
            WorkItemOperation patchOperation = new("add", "/fields/Microsoft.VSTS.Scheduling.StartDate", startDate.ToString());
            patchOperations.Add(patchOperation);
        }
        if (targetDates.TryGetValue(workItemId, out DateTime targetDate))
        {
            WorkItemOperation patchOperation = new("add", "/fields/Microsoft.VSTS.Scheduling.TargetDate", targetDate.ToString());
            patchOperations.Add(patchOperation);
        }
        string url = $"_apis/wit/workitems/{workItemId}?api-version=7.1";
        var content = new StringContent(JsonSerializer.Serialize(patchOperations), System.Text.Encoding.UTF8, "application/json-patch+json");
        var response = await _httpClient.PatchAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Cannot patch the work id {id} with status code {StatusCode}: {content}", workItemId, response.StatusCode, errorContent);
        }
    }

    private static void UpdateDictionaries(ConcurrentDictionary<int, DateTime> startDates, ConcurrentDictionary<int, DateTime> targetDates, WorkItemDetails details)
    {
        if (!details.ParentId.HasValue)
        {
            return;
        }
        int id = details.ParentId.Value;
        if (details.StartDate.HasValue)
        {
            DateTime start = details.StartDate.Value;
            startDates.AddOrUpdate(id, details.StartDate.Value, (key, existingValue) => start < existingValue ? start : existingValue);
        }
        if (details.TargetDate.HasValue)
        {
            DateTime target = details.TargetDate.Value;
            targetDates.AddOrUpdate(id, details.TargetDate.Value, (key, existingValue) => target > existingValue ? target : existingValue);
        }
    }

    private async Task<WorkItemDetails?> GetProductBacklogItemDetails(int id, CancellationToken cancellationToken)
    {
        string url = $"_apis/wit/workitems/{id}?api-version=7.1&fields=System.WorkItemType,Microsoft.VSTS.Scheduling.StartDate,Microsoft.VSTS.Scheduling.TargetDate,System.Parent";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cannot get details for work item {Id}.", id);
            return null;
        }
        string json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("fields", out JsonElement fieldsElement))
        {
            _logger.LogError("Cannot find fields property in json string {json}", json);
            return null;
        }

        DateTime? startDate = null;
        if (fieldsElement.TryGetProperty("Microsoft.VSTS.Scheduling.StartDate", out JsonElement startDateElement) &&
            DateTime.TryParse(startDateElement.GetString(), out DateTime parsedDate))
        {
            startDate = parsedDate;
        }
        else
        {
            return null;
        }

        DateTime? targetDate = null;
        if (fieldsElement.TryGetProperty("Microsoft.VSTS.Scheduling.TargetDate", out JsonElement targetDateElement) &&
            DateTime.TryParse(targetDateElement.GetString(), out parsedDate))
        {
            targetDate = parsedDate;
        }
        else
        {
            return null;
        }

        int? parentId = null;
        if (fieldsElement.TryGetProperty("System.Parent", out JsonElement parentElement) &&
            parentElement.TryGetInt32(out int parsedParentId))
        {
            parentId = parsedParentId;
        }
        else
        {
            _logger.LogWarning("PBI {Id} has no valid parent id: {json}", id, json);
        }

        return new WorkItemDetails(startDate, targetDate, parentId);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
