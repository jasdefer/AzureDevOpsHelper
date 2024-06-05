using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AzureDevOpsHelper.Job.Wiql;
internal record class WorkItemResponse([property: JsonPropertyName("columns")] ImmutableArray<Column> Columns,
    [property: JsonPropertyName("workItems")] ImmutableArray<WorkItemNavigation> WorkItemNavigations);

internal record Column([property: JsonPropertyName("referenceName")] string ReferenceName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);
internal record WorkItemNavigation([property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("url")] string Url);