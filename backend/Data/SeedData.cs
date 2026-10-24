using System.Globalization;
using System.Text.Json;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Models;

namespace VezaSkills.Api.Data;

public static class SeedData
{
    // Looks for CSVs in <app>/data (Docker) or <repo>/data (local dotnet run)
    private static string? FindDataDir()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "data"),
            Path.Combine(Directory.GetCurrentDirectory(), "data"),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static List<Dictionary<string, string>> Read(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!.Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var rows = new List<Dictionary<string, string>>();
        while (csv.Read())
        {
            var d = new Dictionary<string, string>();
            for (var i = 0; i < headers.Length; i++)
                d[headers[i]] = csv.GetField(i)?.Trim() ?? "";
            rows.Add(d);
        }
        return rows;
    }

    private static string Get(Dictionary<string, string> r, string key) =>
        r.TryGetValue(key, out var v) ? v : "";

    public static async Task RunAsync(AppDbContext db, ILogger logger)
    {
        var dir = FindDataDir();
        logger.LogInformation("Seed data directory: {Dir}", dir ?? "(none)");

        // ---- Skills ----
        if (!await db.Skills.AnyAsync())
        {
            var path = dir == null ? null : Path.Combine(dir, "skills.csv");
            if (path != null && File.Exists(path))
            {
                foreach (var r in Read(path))
                {
                    var name = Get(r, "name");
                    if (name == "") continue;
                    db.Skills.Add(new Skill { Name = name, Category = Get(r, "category"), Description = Get(r, "description") });
                }
            }
            else
            {
                // Built-in fallback so the app works before the CSVs arrive
                var fallback = new (string, string, string)[]
                {
                    ("Customer service", "People", "Helping and serving customers politely"),
                    ("Communication", "People", "Explaining things clearly in speech or writing"),
                    ("Teamwork", "People", "Working well with others to get things done"),
                    ("Teaching and tutoring", "People", "Helping others learn"),
                    ("Cash handling", "Practical", "Handling money and giving correct change"),
                    ("Stock and inventory", "Practical", "Counting, storing and tracking goods"),
                    ("Cooking and food preparation", "Practical", "Preparing food safely"),
                    ("Time management", "Personal", "Finishing tasks on time and being punctual"),
                    ("Problem solving", "Personal", "Finding solutions when things go wrong"),
                    ("Computer basics", "Digital", "Using a phone or computer for everyday tasks"),
                };
                foreach (var (n, c, d) in fallback)
                    db.Skills.Add(new Skill { Name = n, Category = c, Description = d });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded skills");
        }

        var skillsByName = await db.Skills.ToDictionaryAsync(s => s.Name.ToLower(), s => s.Id);

        // ---- Roles ----
        if (dir != null && !await db.JobRoles.AnyAsync() && File.Exists(Path.Combine(dir, "roles.csv")))
        {
            foreach (var r in Read(Path.Combine(dir, "roles.csv")))
            {
                var title = Get(r, "title");
                if (title == "") continue;
                var entry = Get(r, "entry_level").ToLower();
                db.JobRoles.Add(new JobRole
                {
                    Title = title, Sector = Get(r, "sector"), Description = Get(r, "description"),
                    EntryLevel = entry == "" || entry == "true" || entry == "1" || entry == "yes"
                });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded roles");
        }

        // ---- Role skills ----
        if (dir != null && !await db.RoleSkills.AnyAsync() && File.Exists(Path.Combine(dir, "role_skills.csv")))
        {
            var rolesByTitle = await db.JobRoles.ToDictionaryAsync(r => r.Title.ToLower(), r => r.Id);
            var seen = new HashSet<(Guid, Guid)>();
            foreach (var r in Read(Path.Combine(dir, "role_skills.csv")))
            {
                if (!rolesByTitle.TryGetValue(Get(r, "role_title").ToLower(), out var roleId)) continue;
                if (!skillsByName.TryGetValue(Get(r, "skill_name").ToLower(), out var skillId)) continue;
                if (!seen.Add((roleId, skillId))) continue;
                _ = int.TryParse(Get(r, "weight"), out var w);
                db.RoleSkills.Add(new RoleSkill { RoleId = roleId, SkillId = skillId, Weight = w <= 0 ? 1 : w });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded role skills");
        }

        // ---- Quiz questions ----
        if (dir != null && !await db.QuizQuestions.AnyAsync() && File.Exists(Path.Combine(dir, "quiz_questions.csv")))
        {
            foreach (var r in Read(Path.Combine(dir, "quiz_questions.csv")))
            {
                if (!skillsByName.TryGetValue(Get(r, "skill_name").ToLower(), out var skillId)) continue;
                var options = new[] { Get(r, "option_a"), Get(r, "option_b"), Get(r, "option_c"), Get(r, "option_d") };
                if (options.Any(o => o == "")) continue;
                if (!int.TryParse(Get(r, "correct_index"), out var correct) || correct < 0 || correct > 3) continue;
                _ = int.TryParse(Get(r, "difficulty"), out var diff);
                var lang = Get(r, "language").ToLower();
                db.QuizQuestions.Add(new QuizQuestion
                {
                    SkillId = skillId,
                    Language = lang == "" ? "en" : lang,
                    Question = Get(r, "question"),
                    OptionsJson = JsonSerializer.Serialize(options),
                    CorrectIndex = correct,
                    Explanation = Get(r, "explanation"),
                    Difficulty = diff <= 0 ? 1 : diff
                });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded quiz questions");
        }
    }
}
