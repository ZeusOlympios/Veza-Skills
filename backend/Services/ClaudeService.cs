using System.Text;
using System.Text.Json;

namespace VezaSkills.Api.Services;

public class ClaudeService
{
    private readonly HttpClient _http;
    private readonly string _key;
    private readonly string _model;
    private readonly ILogger<ClaudeService> _log;

    public ClaudeService(HttpClient http, ILogger<ClaudeService> log)
    {
        _http = http;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(60);
        _key = Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "";
        _model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-sonnet-5-5";
    }

    public static string LanguageName(string code) => code switch
    {
        "zu" => "isiZulu",
        "xh" => "isiXhosa",
        "st" => "Sesotho",
        "tn" => "Setswana",
        "af" => "Afrikaans",
        _ => "English"
    };

    public async Task<string> AskAsync(string system, IEnumerable<(string role, string content)> messages, int maxTokens = 1200)
    {
        if (string.IsNullOrWhiteSpace(_key))
            throw new InvalidOperationException("CLAUDE_API_KEY is not set");

        var body = new
        {
            model = _model,
            max_tokens = maxTokens,
            system,
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", _key);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            // Log status and the API's error message only (never the key)
            _log.LogError("Claude API error {Status}: {Body}", (int)resp.StatusCode, text.Length > 500 ? text[..500] : text);
            throw new InvalidOperationException($"Claude API returned {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(text);
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
                sb.Append(block.GetProperty("text").GetString());
        }
        return sb.ToString();
    }

    // Asks for JSON and extracts the first {...} block from the reply
    public async Task<JsonElement> AskJsonAsync(string system, IEnumerable<(string role, string content)> messages, int maxTokens = 1200)
    {
        var raw = await AskAsync(system, messages, maxTokens);
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Claude did not return JSON");
        using var doc = JsonDocument.Parse(raw.Substring(start, end - start + 1));
        return doc.RootElement.Clone();
    }
}
