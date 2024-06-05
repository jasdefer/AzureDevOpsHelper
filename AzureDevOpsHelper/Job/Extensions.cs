using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzureDevOpsHelper.Job;
internal static class Extensions
{
    public static async Task<string> Query(this HttpClient httpClient, string query, CancellationToken cancellationToken, ILogger logger)
    {
        string url = "_apis/wit/wiql?api-version=7.1";
        var body = new
        {
            query = query
        };
        var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Query to {BaseAddress}{Url} return the status code {StatusCode}. {ErrorContent}", httpClient.BaseAddress, url, response.StatusCode, errorContent);
        }
        string responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }
}
