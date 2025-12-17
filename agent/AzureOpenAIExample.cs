using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using System.Text.Json;

namespace FlockCopilot.Agent;

public class AzureOpenAIExample
{
    public async Task RunDemoAsync(FlockPerformance performance)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

        var client = new OpenAIClient(new Uri(endpoint!), new ApiKeyCredential(key!));

        var perfJson = JsonSerializer.Serialize(performance);

        var messages = new[]
        {
            new ChatMessage(ChatRole.System,
                "You are FlockCopilot, a supply chain diagnostics assistant. " +
                "Explain poultry flock underperformance using the JSON metrics provided. " +
                "Be concise, grounded, and suggest actionable interventions."),
            new ChatMessage(ChatRole.User,
                $"Here is the normalized flock performance JSON:\n```json\n{perfJson}\n```" +
                "\nExplain why this flock is underperforming and suggest remediation steps.")
        };

        var response = await client.GetChatCompletionsAsync(
            deployment,
            new ChatCompletionsOptions
            {
                Messages = { messages[0], messages[1] }
            });

        var content = response.Value.Choices[0].Message.Content[0].Text;
        Console.WriteLine("Model response:");
        Console.WriteLine(content);
    }
}
