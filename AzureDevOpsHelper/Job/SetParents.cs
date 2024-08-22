using AzureDevOpsHelper.Job.Wiql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AzureDevOpsHelper.Job;

public class Relation
{
    public int ChildId { get; set; }
    public int ParentId { get; set; }
}
public class RelationSettings
{
    public List<Relation> Relations { get; set; } = [];
}
public record PatchValue(string Rel, string Url);
public record Patch(string Op, string Path, PatchValue Value);

internal class SetParents : IJob
{
    private const string _rel = "System.LinkTypes.Hierarchy-Reverse";
    private readonly HttpClient _httpClient;
    private readonly ILogger<SetParents> _logger;
    private readonly List<Relation> _relations;

    public SetParents(IHttpClientFactory httpClientFactory,
        ILogger<SetParents> logger,
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
        _logger.LogInformation("Setting parents");
        string query = $"Select [System.Id] From WorkItems Where [System.TeamProject] = '{project}'";
        string json = await _httpClient.Query(query, cancellationToken, _logger);
        WorkItemResponse workItemResponse = JsonSerializer.Deserialize<WorkItemResponse>(json)
            ?? throw new InvalidDataException($"Cannot parse json {json}");
        var dict = workItemResponse.WorkItemNavigations.ToDictionary(x => x.Id, x => x.Url);
        foreach (var relation in _relations)
        {
            if (!dict.TryGetValue(relation.ParentId, out string? parentUrl))
            {
                _logger.LogError("Cannot find the parent {ParentId} in the work items.", relation.ParentId);
            }
            if (!dict.TryGetValue(relation.ChildId, out string? childUrl))
            {
                _logger.LogError("Cannot find the child {ChildId} in the work items.", relation.ChildId);
            }
            if (parentUrl is null || childUrl is null)
            {
                continue;
            }
            PatchValue patchValue = new(_rel, parentUrl);
            Patch[] patches = [new("add", "/relations/-", patchValue)];
            string url = $"_apis/wit/workitems/{relation.ChildId}?api-version=7.1";
            var content = new StringContent(JsonSerializer.Serialize(patches), System.Text.Encoding.UTF8, "application/json-patch+json");
            var response = await _httpClient.PatchAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Cannot patch the work item id {id} with status code {StatusCode}: {content}", relation.ChildId, response.StatusCode, errorContent);
            }
        }
    }
}
