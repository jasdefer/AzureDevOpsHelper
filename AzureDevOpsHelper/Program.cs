using AzureDevOpsHelper;
using AzureDevOpsHelper.Job;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AzureDevOpsSettings>(builder.Configuration.GetSection("AzureDevOps"));
builder.Services.AddTransient<IJob, SetParentDates>();
builder.Services.AddHttpClient("AzureDevOps", (serviceProvider, httpClient) =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<AzureDevOpsSettings>>().Value;
    string baseUrl = settings.BaseUrl ?? throw new ArgumentNullException("Base Url not defined.");
    string organization = settings.Organization ?? throw new ArgumentNullException("Azure DevOps organization not defined.");
    string projectName = settings.ProjectName ?? throw new ArgumentNullException("Azure DevOps project name not defined.");
    string uriString = $"{baseUrl}{organization}/{Uri.EscapeDataString(projectName)}/";
    httpClient.BaseAddress = new Uri(uriString);
    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{settings.AccessToken}"));
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
});

builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();