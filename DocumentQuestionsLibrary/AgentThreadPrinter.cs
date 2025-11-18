using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.AI.Projects; // for Foundry fallback
using Azure.AI.Projects.Agents; // agents/threads/messages
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Azure.AI.Agents.Persistent;   // ChatMessage & AIContent types

public static class ThreadPrinter
{
    public static async Task PrintAsync(AgentThread thread, CancellationToken ct = default)
    {
        // 1) Preferred path: use a ChatMessageStore attached to the thread (in-memory or custom)
        // ChatMessageStore returns messages in ascending chronological order (oldest first).
        // https://learn.microsoft.com/.../chatmessagestore.getmessagesasync
        var store = thread.GetService<ChatMessageStore>(); // may be null in service-backed threads
        IEnumerable<ChatMessage>? msgs = null;

        if (store != null)
        {
            msgs = await store.GetMessagesAsync(ct);
        }

        // 2) Fallback for service-backed threads (e.g., Foundry Agent Service):
        // Try to obtain a service-side conversation/thread id from AgentThreadMetadata.
        // The AgentThread exposes GetService<T> for associated services/metadata.
        if (msgs == null || !msgs.Any())
        {
            var meta = thread.GetService<AgentThreadMetadata>();
            var serviceThreadId = meta?.ConversationId;   // conventional place for remote thread id

            var endpoint = Environment.GetEnvironmentVariable("AI_FOUNDRY_PROJECT_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(serviceThreadId) && !string.IsNullOrWhiteSpace(endpoint))
            {
                var cred = new DefaultAzureCredential();

                // Use Azure AI Foundry Projects SDK to enumerate messages from the remote thread.
                // (Agents → Threads → Messages)
                var projectClient = new AIProjectClient(new Uri(endpoint), cred);
                var agentsClient  = projectClient.GetAgentsClient();
                var threadsClient = agentsClient.GetThreadsClient();
                var messagesClient = threadsClient.GetMessagesClient(serviceThreadId);

                var collected = new List<AgentMessage>();
                await foreach (var m in messagesClient.GetMessagesAsync(serviceThreadId, ct))
                {
                    collected.Add(m);
                }

                // Convert service messages → Microsoft.Extensions.AI ChatMessage for a uniform printer.
                msgs = collected
                    .OrderBy(m => m.CreatedAt)
                    .SelectMany(ConvertAgentMessageToChatMessages)   // many-to-1 for multi-part content
                    .ToList();
            }
        }

        if (msgs == null || !msgs.Any())
        {
            Console.WriteLine("(no messages to print)");
            return;
        }

        foreach (var m in msgs)
        {
            var ts = m.CreatedAt?.ToString("o") ?? "";
            Console.WriteLine($"{ts} [{m.Role}] {(!string.IsNullOrEmpty(m.AuthorName) ? $"<{m.AuthorName}>" : "")}");

            if (m.Contents != null && m.Contents.Count > 0)
            {
                foreach (var c in m.Contents)
                {
                    switch (c)
                    {
                        case TextContent t:
                            Console.WriteLine(t.Text);
                            break;
                        case ImageContent i:
                            Console.WriteLine($"[image] {i.MediaType} {(i.Uri != null ? i.Uri : $"bytes:{i.Data?.Length}")}");
                            break;
                        case FunctionCallContent fc:
                            Console.WriteLine($"[tool-call] {fc.Name} args: {System.Text.Json.JsonSerializer.Serialize(fc.Arguments)}");
                            break;
                        case FunctionResultContent fr:
                            Console.WriteLine($"[tool-result] {fr.Name} -> {fr.Result}");
                            break;
                        default:
                            Console.WriteLine($"[{c.GetType().Name}]");
                            break;
                    }
                }
            }
            else
            {
                Console.WriteLine("(no content)");
            }

            // Additional properties if present
            if (m.AdditionalProperties?.Count > 0)
            {
                foreach (var kv in m.AdditionalProperties)
                    Console.WriteLine($"  meta:{kv.Key}={kv.Value}");
            }

            Console.WriteLine(new string('-', 60));
        }
    }

    // Map Azure Foundry AgentService messages to Microsoft.Extensions.AI ChatMessage(s)
    private static IEnumerable<ChatMessage> ConvertAgentMessageToChatMessages(AgentMessage src)
    {
        // Basic role mapping; expand for more content types as needed.
        var role = src.Role?.ToLowerInvariant() switch
        {
            "user"      => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "tool"      => ChatRole.Tool,
            "system"    => ChatRole.System,
            _           => ChatRole.Assistant
        };

        var result = new ChatMessage(role, new List<AIContent>());

        if (!string.IsNullOrEmpty(src.Name))
            result.AuthorName = src.Name;

        // Map content parts from Foundry → AIContent (text/tool-call/tool-result/images)
        if (src.Content != null)
        {
            foreach (var part in src.Content)
            {
                switch (part)
                {
                    case MessageTextContent text:
                        result.Contents.Add(new TextContent(text.Text));
                        break;

                    case MessageImageContent img:
                        if (img.ImageUri != null)
                            result.Contents.Add(new ImageContent(img.ImageUri, img.MediaType));
                        else if (img.Bytes != null)
                            result.Contents.Add(new ImageContent(img.Bytes, img.MediaType));
                        break;

                    case MessageToolCallContent tc:
                        result.Contents.Add(new FunctionCallContent(tc.CallId, tc.ToolName, tc.Arguments));
                        break;

                    case MessageToolResultContent tr:
                        result.Contents.Add(new FunctionResultContent(tr.CallId, tr.ToolName, tr.Output));
                        break;

                    default:
                        // ignore unknown types
                        break;
                }
            }
        }

        // Attach timestamps/ids if provided
        if (src.CreatedAt != default) result.CreatedAt = src.CreatedAt;

        return new[] { result };
    }
}