using System.Text.Json;

namespace VezaSkills.Api.Services;

/// <summary>
/// Template-based replacement for the AI calls, used only when Claude is unavailable
/// (no API credit, no key, network error). It produces the same JSON shapes the
/// controllers already parse, so no other code changes. This is "demo mode":
/// keyword matching and fixed questions, NOT AI. Say so in the pitch and SSDLC.
/// </summary>
public static class DemoFallback
{
    // keyword (lowercase) -> skill name (must match skills.csv exactly)
    private static readonly (string skill, string[] words)[] Map =
    {
        ("Customer service", new[] { "customer", "shop", "spaza", "serve", "serving", "client", "restaurant", "waiter", "helping people" }),
        ("Cash handling",    new[] { "money", "cash", "till", "change", "pay", "payment", "cashier", "count" }),
        ("Teamwork",         new[] { "team", "together", "group", "sport", "soccer", "netball", "choir", "volunteer" }),
        ("Communication",    new[] { "tutor", "teach", "explain", "translate", "speak", "talk", "present", "write", "letter" }),
        ("Basic computer skills", new[] { "computer", "laptop", "internet", "email", "typing", "type ", "word", "google" }),
        ("Spreadsheet basics", new[] { "excel", "spreadsheet", "budget", "table", "sheets" }),
        ("Online safety",    new[] { "password", "scam", "online safety", "privacy", "phishing" }),
        ("Time management",  new[] { "plan", "schedule", "deadline", "on time", "early", "routine", "timetable" }),
        ("Problem solving",  new[] { "fix", "solve", "repair", "problem", "figure out", "troubleshoot" }),
        ("Leadership",       new[] { "lead", "captain", "organis", "organiz", "manage", "in charge", "prefect", "coordinate" }),
    };

    public static string ChatJson(string latestMessage, string allUserText, IEnumerable<string> validSkillNames, int priorUserTurns)
    {
        var valid = new HashSet<string>(validSkillNames, StringComparer.OrdinalIgnoreCase);
        var text = (allUserText ?? "").ToLowerInvariant();
        var evidence = (latestMessage ?? "").Trim();
        if (evidence.Length > 250) evidence = evidence[..250];

        var found = new List<string>();
        foreach (var (skill, words) in Map)
            if (valid.Contains(skill) && words.Any(w => text.Contains(w))) found.Add(skill);
        found = found.Take(3).ToList();

        string prefix = found.Count > 0
            ? "I can see skills like " + string.Join(" and ", found) + ". "
            : "Thank you for sharing. ";

        string question = priorUserTurns switch
        {
            0 => "How often do you do this, and who does it help?",
            1 => "What was the best result of what you did?",
            2 => "Is there anything else you do, like helping at home, a course or volunteering?",
            _ => "You can add these skills to your profile and build your CV now."
        };

        var suggested = found.Select(s => new { name = s, evidenceText = evidence, confidence = 0.6 }).ToArray();
        return JsonSerializer.Serialize(new { reply = prefix + question, suggestedSkills = suggested });
    }

    public static JsonElement CvJson(IEnumerable<(string name, string evidence)> skills)
    {
        var list = skills.Where(s => !string.IsNullOrWhiteSpace(s.name)).ToList();
        var names = list.Select(s => s.name).ToList();

        string joined = names.Count switch
        {
            0 => "everyday life",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
        };

        var headline = names.Count >= 2
            ? $"Entry-level candidate with {names[0]} and {names[1]} skills"
            : names.Count == 1 ? $"Entry-level candidate with {names[0]} skills" : "Entry-level candidate";
        if (headline.Length > 90) headline = headline[..90];

        var summary = $"I have practical experience in {joined} from everyday life. I am ready to apply these skills in an entry-level role.";

        var bullets = new Dictionary<string, string[]>();
        foreach (var (name, evidence) in list)
        {
            var e = (evidence ?? "").Trim();
            if (e.Length == 0) continue;
            if (e.Length > 200) e = e[..200];
            bullets[name] = new[] { e };
        }

        var json = JsonSerializer.Serialize(new { headline, summary, bullets });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
