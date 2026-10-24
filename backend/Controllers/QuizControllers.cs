using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Data;
using VezaSkills.Api.Models;
using VezaSkills.Api.Services;

namespace VezaSkills.Api.Controllers;

[ApiController]
[Route("api/quiz")]
[Authorize]
public class QuizController : ApiBase
{
    private readonly ClaudeService _claude;
    public QuizController(AppDbContext db, ClaudeService claude) : base(db) { _claude = claude; }

    public record StartReq(Guid SkillId, string? Language);
    public record AnswerReq(Guid QuestionId, int SelectedIndex);
    public record SubmitReq(List<AnswerReq>? Answers);

    private const int QuestionsPerQuiz = 5;

    // Asks Claude for questions when the question bank is thin, and saves them for reuse
    private async Task<List<QuizQuestion>> GenerateQuestions(Skill skill, string lang)
    {
        var system =
            "You write short multiple-choice questions that check a practical skill for an entry-level worker in South Africa. " +
            "Write in " + ClaudeService.LanguageName(lang) + " using plain, simple language. " +
            "Use realistic everyday workplace scenarios, not theory. Each question has exactly 4 options and exactly one correct answer. " +
            "Return JSON only and nothing else: " +
            "{\"questions\": [{\"question\": \"...\", \"options\": [\"...\", \"...\", \"...\", \"...\"], \"correctIndex\": <0 to 3>, \"explanation\": \"<one short sentence>\"}]}";
        var user = "Skill: " + skill.Name + (string.IsNullOrWhiteSpace(skill.Description) ? "" : " (" + skill.Description + ")") +
                   "\nWrite " + QuestionsPerQuiz + " questions.";

        var root = await _claude.AskJsonAsync(system, new[] { ("user", user) }, 2000);
        var created = new List<QuizQuestion>();
        if (root.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in qs.EnumerateArray())
            {
                if (!q.TryGetProperty("question", out var qt) || !q.TryGetProperty("options", out var opt) || opt.ValueKind != JsonValueKind.Array) continue;
                var options = opt.EnumerateArray().Select(o => o.GetString() ?? "").ToList();
                if (options.Count != 4 || options.Any(o => o.Length == 0)) continue;
                if (!q.TryGetProperty("correctIndex", out var ci) || ci.ValueKind != JsonValueKind.Number) continue;
                var idx = ci.GetInt32();
                if (idx < 0 || idx > 3) continue;
                created.Add(new QuizQuestion
                {
                    SkillId = skill.Id,
                    Language = lang,
                    Question = qt.GetString() ?? "",
                    OptionsJson = JsonSerializer.Serialize(options),
                    CorrectIndex = idx,
                    Explanation = q.TryGetProperty("explanation", out var ex) ? (ex.GetString() ?? "") : "",
                    Difficulty = 1
                });
            }
        }
        if (created.Count > 0)
        {
            Db.QuizQuestions.AddRange(created);
            await Db.SaveChangesAsync();
        }
        return created;
    }

    [HttpPost("start")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Start([FromBody] StartReq req)
    {
        var (user, profile) = await EnsureUserAsync();
        var skill = await Db.Skills.FirstOrDefaultAsync(s => s.Id == req.SkillId);
        if (skill == null) return Err(404, "not_found", "Skill not found.");
        if (!await Db.ProfileSkills.AnyAsync(p => p.ProfileId == profile.Id && p.SkillId == req.SkillId))
            return Err(400, "skill_not_on_profile", "Add this skill to your profile first.");

        var lang = NormLang(req.Language ?? user.PreferredLanguage);
        var bank = await Db.QuizQuestions.Where(q => q.SkillId == skill.Id && q.Language == lang).ToListAsync();

        if (bank.Count < QuestionsPerQuiz)
        {
            try { bank.AddRange(await GenerateQuestions(skill, lang)); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
            {
                if (bank.Count == 0 && lang != "en")
                    bank = await Db.QuizQuestions.Where(q => q.SkillId == skill.Id && q.Language == "en").ToListAsync();
            }
        }
        if (bank.Count == 0) return Err(503, "no_questions", "No questions are available for this skill yet.");

        var picked = bank.OrderBy(_ => Random.Shared.Next()).Take(QuestionsPerQuiz).ToList();
        var attempt = new QuizAttempt
        {
            ProfileId = profile.Id,
            SkillId = skill.Id,
            QuestionIdsJson = JsonSerializer.Serialize(picked.Select(q => q.Id)),
            Total = picked.Count
        };
        Db.QuizAttempts.Add(attempt);
        await Db.SaveChangesAsync();

        // Correct answers are never sent here
        var questions = picked.Select(q => new
        {
            id = q.Id,
            question = q.Question,
            options = ProfileService.ParseStrings(q.OptionsJson)
        });
        return Ok(new { attemptId = attempt.Id, questions });
    }

    [HttpPost("{attemptId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid attemptId, [FromBody] SubmitReq req)
    {
        var (_, profile) = await EnsureUserAsync();
        var attempt = await Db.QuizAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.ProfileId == profile.Id);
        if (attempt == null) return Err(404, "not_found", "Quiz attempt not found.");
        if (attempt.SubmittedAt != null) return Err(400, "already_submitted", "This quiz was already submitted.");

        var ids = ProfileService.ParseIds(attempt.QuestionIdsJson);
        var questions = await Db.QuizQuestions.Where(q => ids.Contains(q.Id)).ToListAsync();
        var answers = (req.Answers ?? new List<AnswerReq>())
            .Where(a => ids.Contains(a.QuestionId))
            .GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.First().SelectedIndex);

        var results = new List<object>();
        var score = 0;
        foreach (var id in ids)
        {
            var q = questions.FirstOrDefault(x => x.Id == id);
            if (q == null) continue;
            var correct = answers.TryGetValue(id, out var sel) && sel == q.CorrectIndex;
            if (correct) score++;
            results.Add(new { questionId = id, correct, explanation = q.Explanation });
        }

        var total = ids.Count;
        var passed = total > 0 && score * 100 >= 60 * total;
        attempt.AnswersJson = JsonSerializer.Serialize(answers.Select(kv => new { questionId = kv.Key, selectedIndex = kv.Value }));
        attempt.Score = score;
        attempt.Total = total;
        attempt.Passed = passed;
        attempt.SubmittedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { score, total, passed, results });
    }

    [HttpGet("results")]
    public async Task<IActionResult> Results()
    {
        var (_, profile) = await EnsureUserAsync();
        var attempts = await Db.QuizAttempts
            .Where(a => a.ProfileId == profile.Id && a.SubmittedAt != null)
            .OrderByDescending(a => a.SubmittedAt).ToListAsync();
        var skillIds = attempts.Select(a => a.SkillId).Distinct().ToList();
        var names = await Db.Skills.Where(s => skillIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
        var list = attempts.Select(a => new
        {
            attemptId = a.Id,
            skillId = a.SkillId,
            skillName = names.GetValueOrDefault(a.SkillId, ""),
            score = a.Score,
            total = a.Total,
            passed = a.Passed,
            submittedAt = a.SubmittedAt
        });
        return Ok(new { attempts = list });
    }
}

[ApiController]
[Route("api/matches")]
[Authorize]
public class MatchesController : ApiBase
{
    public MatchesController(AppDbContext db) : base(db) { }

    public record MatchDto(Guid RoleId, string Title, int MatchPercent, List<string> MissingSkills);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (_, profile) = await EnsureUserAsync();
        var mine = (await Db.ProfileSkills.Where(p => p.ProfileId == profile.Id).Select(p => p.SkillId).ToListAsync()).ToHashSet();
        var roles = await Db.JobRoles.Where(r => r.EntryLevel).ToListAsync();
        var roleSkills = await Db.RoleSkills.ToListAsync();
        var names = await Db.Skills.ToDictionaryAsync(s => s.Id, s => s.Name);

        var matches = new List<MatchDto>();
        foreach (var r in roles)
        {
            var req = roleSkills.Where(x => x.RoleId == r.Id).ToList();
            var total = req.Sum(x => x.Weight);
            if (total == 0) continue;
            var got = req.Where(x => mine.Contains(x.SkillId)).Sum(x => x.Weight);
            var pct = (int)Math.Round(100.0 * got / total);
            var missing = req.Where(x => !mine.Contains(x.SkillId)).OrderByDescending(x => x.Weight)
                .Select(x => names.GetValueOrDefault(x.SkillId, "")).Where(n => n.Length > 0).ToList();
            matches.Add(new MatchDto(r.Id, r.Title, pct, missing));
        }
        return Ok(new { matches = matches.OrderByDescending(m => m.MatchPercent).Take(5).ToList() });
    }
}
