using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using ModelContextProtocol.Server;

namespace MCPServer.MCPTools;

public record TestCaseResult(string Title, string Outcome, double DurationMs, string? ErrorMessage = null, string? StackTrace = null);

public record GetTestCaseResultsResponse(
    bool Success,
    string LogMessages,
    List<TestCaseResult> TestResults,
    string? ErrorMessage = null);

[McpServerToolType]
public class AzureDevOpsTools
{   
    /// <summary>
    /// Get test case results from the latest successful/partially successful build of a pipeline/definition.
    /// </summary>
    [McpServerTool, Description("Retrieve test case results from the latest successful build of a pipeline/definition in Azure DevOps.")]
    public static async Task<GetTestCaseResultsResponse> GetTestCaseResultsAsync(
        string projectName,
        string definitionName,
        string testCaseTitle)
    {
        var logMessages = new List<string>();
        var testResults = new List<TestCaseResult>();
        
        try
        {
            logMessages.Add($"Starting GetTestCaseResults for project: {projectName}, definition: {definitionName}, testCase: {testCaseTitle}");
            
            // Validate inputs and elicit missing parameters
            if (string.IsNullOrWhiteSpace(projectName))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Project name is required");
            if (string.IsNullOrWhiteSpace(definitionName))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Definition (pipeline) name is required");
            if (string.IsNullOrWhiteSpace(testCaseTitle))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "Test case title is required");
            
            // Read environment variables
            string? collectionUrl = Environment.GetEnvironmentVariable("AZURE_DEVOPS_COLLECTION_URL");
            string? pat = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
            if (string.IsNullOrWhiteSpace(collectionUrl) || string.IsNullOrWhiteSpace(pat))
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, "AZURE_DEVOPS_COLLECTION_URL and AZURE_DEVOPS_PAT must be set");
            
            logMessages.Add($"Using Azure DevOps collection URL: {collectionUrl}");
            
            // Connect using PAT
            var creds = new VssBasicCredential(string.Empty, pat);
            var connection = new VssConnection(new Uri(collectionUrl), creds);
            
            logMessages.Add("Connecting to Azure DevOps...");
            var buildClient = await connection.GetClientAsync<BuildHttpClient>();
            var testClient = await connection.GetClientAsync<TestManagementHttpClient>();
            logMessages.Add("Successfully connected to Azure DevOps");
            
            // Find build definition by name
            logMessages.Add($"Looking for build definition: {definitionName}");
            var definitions = await buildClient.GetDefinitionsAsync(project: projectName, name: definitionName);
            var definition = definitions.FirstOrDefault();
            if (definition == null)
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults, $"Build definition '{definitionName}' not found in project '{projectName}'.");
            
            logMessages.Add($"Found build definition with ID: {definition.Id}");
            
            logMessages.Add($"Getting latest successful or partially successful build...");
            var builds = await buildClient.GetBuildsAsync(
                project: projectName,
                definitions: [definition.Id],
                resultFilter: BuildResult.Succeeded | BuildResult.PartiallySucceeded,
                statusFilter: BuildStatus.Completed,
                branchName: null,
                top: 1);
            
            var build = builds.FirstOrDefault();
            if (build == null)
            {
                // If no build found with the specified branch, let's try without branch filter to see what branches exist
                logMessages.Add($"No builds found.");
                
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                    $"No completed successful or partially successful build found for definition '{definitionName}'.");
            }
            
            logMessages.Add($"Found build ID: {build.Id}, Build Number: {build.BuildNumber}");
            
            logMessages.Add("Getting test runs for build...");
            var testRuns = await testClient.GetTestRunsAsync(projectName, buildUri: build.Uri.ToString());
            logMessages.Add($"Found {testRuns.Count} test runs");
            
            foreach (var run in testRuns)
            {
                var runResults = await testClient.GetTestResultsAsync(projectName, run.Id);
                foreach (var r in runResults)
                {
                    if (!string.IsNullOrWhiteSpace(r.TestCaseTitle) &&
                        r.TestCaseTitle.Contains(testCaseTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        logMessages.Add($"Found matching test case: {r.TestCaseTitle}, Outcome: {r.Outcome}");
						testResults.Add(new TestCaseResult(
                            r.TestCaseTitle!,
                            r.Outcome,
                            r.DurationInMs,
                            r.ErrorMessage,
                            r.StackTrace));
                    }
                }
            }
            
            if (testResults.Count == 0)
            {
                logMessages.Add($"No test case results matching '{testCaseTitle}' were found");
                return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                    $"No test case results matching '{testCaseTitle}' were found.");
            }
            
            logMessages.Add($"Successfully found {testResults.Count} matching test case results");
            return new GetTestCaseResultsResponse(true, string.Join("\n", logMessages), testResults);
        }
        catch (Exception ex)
        {
            logMessages.Add($"ERROR: {ex.GetType().Name}: {ex.Message}");
            logMessages.Add($"Stack trace: {ex.StackTrace}");
            return new GetTestCaseResultsResponse(false, string.Join("\n", logMessages), testResults,
                $"Exception occurred: {ex.GetType().Name}: {ex.Message}");
        }
    }
}