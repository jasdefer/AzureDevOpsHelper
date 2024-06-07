namespace AzureDevOpsHelper.Job.Model;
internal record WorkItemOperation(string Op, string Path, string Value);
public record PatchValue(string Rel, string Url);
public record Patch(string Op, string Path, PatchValue Value);