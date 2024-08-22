using AzureDevOpsHelper.Job.Wiql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace AzureDevOpsHelper.Job;

public record WorkItemDetail(int id, Dictionary<string, object> fields);

internal class AddTags : IJob
{
    private const string _rel = "System.LinkTypes.Hierarchy-Reverse";
    private readonly HttpClient _httpClient;
    private readonly ILogger<AddTags> _logger;
    private readonly List<Relation> _relations;

    public AddTags(IHttpClientFactory httpClientFactory,
        ILogger<AddTags> logger,
        IOptions<RelationSettings> options)
    {
        _httpClient = httpClientFactory.CreateClient("AzureDevOps");
        _logger = logger;
        _relations = options.Value.Relations;
    }
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task Run(string project, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Add tags to features and PBIs");
        var tagsByEpicId = await GetTagsByEpicId(project, cancellationToken);
        await SetFeatureTags(tagsByEpicId, project, cancellationToken);
    }

    private async Task SetPbiTags(int featureId, string tag, string project, CancellationToken cancellationToken)
    {
        string pbiQuery = $"Select [System.Id], [System.Title], [System.WorkItemType] From WorkItems Where ([System.WorkItemType] = 'Product Backlog Item' OR [System.WorkItemType] = 'Bug') AND [System.Parent] = {featureId} AND [System.TeamProject] = '{project}'";
        string pbiJson = await _httpClient.Query(pbiQuery, cancellationToken, _logger);
        WorkItemResponse pbiResponse = JsonSerializer.Deserialize<WorkItemResponse>(pbiJson)
            ?? throw new InvalidDataException($"Cannot parse json {pbiJson}");

        var pbiDict = pbiResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);

        foreach (var pbiUrl in pbiDict.Values)
        {
            var response = await _httpClient.GetAsync($"{pbiUrl}?$expand=all&$select=System.Tags,System.ParentId");
            string content = await response.Content.ReadAsStringAsync();
            WorkItemDetail pbiDetail = JsonSerializer.Deserialize<WorkItemDetail>(content)
                ?? throw new InvalidDataException($"Cannot parse work item detail json {content}");

            string existingTags = pbiDetail.fields.ContainsKey("System.Tags") ? pbiDetail.fields["System.Tags"].ToString()! : string.Empty;
            await SetTaskTags(pbiDetail.id, tag, project, cancellationToken);
            if (existingTags.Split(';').Any(x => x.Trim() == tag))
            {
                continue;
            }

            string newTags = string.IsNullOrEmpty(existingTags) ? tag : $"{existingTags}; {tag}";

            var patchDocument = new[]
            {
                new { op = "add", path = "/fields/System.Tags", value = newTags }
            };

            var patchContent = new StringContent(JsonSerializer.Serialize(patchDocument), Encoding.UTF8, "application/json-patch+json");
            var patchResponse = await _httpClient.PatchAsync($"{pbiUrl}?api-version=7.1", patchContent);
            if (!patchResponse.IsSuccessStatusCode)
            {
                string patchResponseContent = await patchResponse.Content.ReadAsStringAsync();
                _logger.LogError("Cannot patch PBI {StatusCode}: {Content}", patchResponse.StatusCode, patchResponseContent);
            }

            _logger.LogInformation($"Updated PBI {pbiDetail.id} with tags: {newTags}");
        }
    }

    private async Task SetFeatureTags(FrozenDictionary<int, string> tagsByEpicId, string project, CancellationToken cancellationToken)
    {
        string featureQuery = $"Select [System.Id], [System.Title], [System.WorkItemType], [System.Parent] From WorkItems Where [System.WorkItemType] = 'Feature' AND [System.TeamProject] = '{project}'";
        string featureJson = await _httpClient.Query(featureQuery, cancellationToken, _logger);
        WorkItemResponse featureResponse = JsonSerializer.Deserialize<WorkItemResponse>(featureJson)
            ?? throw new InvalidDataException($"Cannot parse json {featureJson}");

        var featureDict = featureResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);

        foreach (var featureUrl in featureDict.Values)
        {
            var response = await _httpClient.GetAsync($"{featureUrl}?$expand=all&$select=System.Parent,System.Tags");
            string content = await response.Content.ReadAsStringAsync();
            WorkItemDetail featureDetail = JsonSerializer.Deserialize<WorkItemDetail>(content)
                ?? throw new InvalidDataException($"Cannot parse work item detail json {content}");

            // Check the parent Epic's ID
            int parentId = int.Parse(featureDetail.fields["System.Parent"].ToString()!);
            string tag = tagsByEpicId[parentId];
            // Add the tag to the Feature's existing tags
            string existingTags = featureDetail.fields.ContainsKey("System.Tags") ? featureDetail.fields["System.Tags"].ToString()! : string.Empty;
            await SetPbiTags(featureDetail.id, tag, project, cancellationToken);
            if (existingTags.Split(';').Any(x => x.Trim() == tag))
            {
                continue;
            }

            string newTags = string.IsNullOrEmpty(existingTags) ? tag : $"{existingTags}; {tag}";
            // Update the Feature with the new tags
            var patchDocument = new[]
            {
                new { op = "add", path = "/fields/System.Tags", value = newTags }
            };

            var patchContent = new StringContent(JsonSerializer.Serialize(patchDocument), Encoding.UTF8, "application/json-patch+json");
            var patchResponse = await _httpClient.PatchAsync($"{featureUrl}?api-version=7.1", patchContent);
            if (!patchResponse.IsSuccessStatusCode)
            {
                string patchResponseContent = await patchResponse.Content.ReadAsStringAsync();
                _logger.LogError("Cannot patch feature {StatusCode}: {Content}", patchResponse.StatusCode, patchResponseContent);
            }

            _logger.LogInformation($"Updated Feature {featureDetail.id} with tags: {newTags}");
        }
    }

    private async Task SetTaskTags(int pbiId, string tag, string project, CancellationToken cancellationToken)
    {
        string taskQuery = $"Select [System.Id], [System.Title], [System.WorkItemType] From WorkItems Where [System.WorkItemType] = 'Task' AND [System.Parent] = {pbiId} AND [System.TeamProject] = '{project}'";
        string taskJson = await _httpClient.Query(taskQuery, cancellationToken, _logger);
        WorkItemResponse taskResponse = JsonSerializer.Deserialize<WorkItemResponse>(taskJson)
            ?? throw new InvalidDataException($"Cannot parse json {taskJson}");

        var taskDict = taskResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);

        foreach (var taskUrl in taskDict.Values)
        {
            var response = await _httpClient.GetAsync($"{taskUrl}?$expand=all&$select=System.Tags,System.ParentId");
            string content = await response.Content.ReadAsStringAsync();
            WorkItemDetail taskDetail = JsonSerializer.Deserialize<WorkItemDetail>(content)
                ?? throw new InvalidDataException($"Cannot parse work item detail json {content}");

            string existingTags = taskDetail.fields.ContainsKey("System.Tags") ? taskDetail.fields["System.Tags"].ToString()! : string.Empty;

            if (existingTags.Split(';').Any(x => x.Trim() == tag))
            {
                continue;
            }

            string newTags = string.IsNullOrEmpty(existingTags) ? tag : $"{existingTags}; {tag}";

            var patchDocument = new[]
            {
                new { op = "add", path = "/fields/System.Tags", value = newTags }
            };

            var patchContent = new StringContent(JsonSerializer.Serialize(patchDocument), Encoding.UTF8, "application/json-patch+json");
            var patchResponse = await _httpClient.PatchAsync($"{taskUrl}?api-version=7.1", patchContent);
            if (!patchResponse.IsSuccessStatusCode)
            {
                string patchResponseContent = await patchResponse.Content.ReadAsStringAsync();
                _logger.LogError("Cannot patch Task {StatusCode}: {Content}", patchResponse.StatusCode, patchResponseContent);
            }

            _logger.LogInformation($"Updated Task {taskDetail.id} with tags: {newTags}");
        }
    }

    private async Task<FrozenDictionary<int, string>> GetTagsByEpicId(string project, CancellationToken cancellationToken)
    {
        string query = $"Select [System.Id], [System.Title], [System.WorkItemType] From WorkItems Where [System.WorkItemType] = 'Epic' AND [System.TeamProject] = '{project}'";
        string json = await _httpClient.Query(query, cancellationToken, _logger);
        WorkItemResponse workItemResponse = JsonSerializer.Deserialize<WorkItemResponse>(json)
            ?? throw new InvalidDataException($"Cannot parse json {json}");
        var dict = workItemResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);
        Dictionary<int, string> tagsPerEpicId = dict.ToDictionary(x => x.Key, x => "");
        foreach (var url in dict.Values)
        {
            var response = await _httpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();
            WorkItemDetail workItemDetail = JsonSerializer.Deserialize<WorkItemDetail>(content)
               ?? throw new InvalidDataException($"Cannot parse work item detail json {content}");
            tagsPerEpicId[workItemDetail.id] = "Task " + workItemDetail.fields["System.Title"].ToString()![..2].ToString()!;
        }
        return tagsPerEpicId.ToFrozenDictionary();
    }
}
