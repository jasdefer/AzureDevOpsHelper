using AzureDevOpsHelper.Job.Helper;
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

internal class SetParents : IJob
{
    
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
            await _httpClient.SetParent(_logger, parentUrl, relation.ChildId, cancellationToken);
        }
    }
}
