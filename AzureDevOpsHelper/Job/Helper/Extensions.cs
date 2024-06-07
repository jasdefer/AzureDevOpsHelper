using AzureDevOpsHelper.Job.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzureDevOpsHelper.Job.Helper;
internal static class Extensions
{
    private const string _parentRelation = "System.LinkTypes.Hierarchy-Reverse";
    public static async Task<string> Query(this HttpClient httpClient, string query, CancellationToken cancellationToken, ILogger logger)
    {
        string url = "_apis/wit/wiql?api-version=7.1";
        var body = new
        {
            query
        };
        var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Query to {BaseAddress}{Url} return the status code {StatusCode}. {ErrorContent}", httpClient.BaseAddress, url, response.StatusCode, errorContent);
            throw new Exception(errorContent);
        }
        string responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }

    public static async Task SetParent(this HttpClient httpClient, ILogger logger, string parentUrl, int childId, CancellationToken cancellationToken)
    {
        PatchValue patchValue = new(_parentRelation, parentUrl);
        Patch[] patches = [new("add", "/relations/-", patchValue)];
        string url = $"_apis/wit/workitems/{childId}?api-version=7.1";
        var content = new StringContent(JsonSerializer.Serialize(patches), System.Text.Encoding.UTF8, "application/json-patch+json");
        var response = await httpClient.PatchAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Cannot patch the work item id {id} with status code {StatusCode}: {content}", childId, response.StatusCode, errorContent);
        }
    }
}
