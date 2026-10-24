using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Data;
using VezaSkills.Api.Models;

namespace VezaSkills.Api.Services;

public record SkillDto(Guid Id, Guid SkillId, string Name, string EvidenceText, string Status, int? QuizScore, int? QuizTotal, int VouchCount);
public record ProfileDto(Guid Id, string? Headline, string? Summary, bool IsPublic, string ShareSlug, List<SkillDto> Skills);

public class ProfileService
{
    private readonly AppDbContext _db;
    public ProfileService(AppDbContext db) { _db = db; }

    public static List<Guid> ParseIds(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new List<Guid>() : (JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>());

    public static List<string> ParseStrings(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new List<string>() : (JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>());

    // Status is computed: checked = passed the skills check, vouched = at least one vouch,
    // verified = both, claimed = neither.
    public async Task<List<SkillDto>> BuildSkills(Guid profileId)
    {
        var pskills = await _db.ProfileSkills.Include(p => p.Skill)
            .Where(p => p.ProfileId == profileId).OrderBy(p => p.CreatedAt).ToListAsync();
        var attempts = await _db.QuizAttempts
            .Where(a => a.ProfileId == profileId && a.SubmittedAt != null).ToListAsync();
        var vouches = await _db.Vouches.Where(v => v.ProfileId == profileId).ToListAsync();
        var confirmed = vouches.Select(v => ParseIds(v.ConfirmedSkillIdsJson)).ToList();

        var result = new List<SkillDto>();
        foreach (var ps in pskills)
        {
            var forSkill = attempts.Where(a => a.SkillId == ps.SkillId).ToList();
            var best = forSkill.OrderByDescending(a => a.Score ?? 0).FirstOrDefault();
            var checkedOk = forSkill.Any(a => a.Passed == true);
            var vouchCount = confirmed.Count(c => c.Contains(ps.SkillId));
            var status = checkedOk && vouchCount > 0 ? "verified"
                : checkedOk ? "checked"
                : vouchCount > 0 ? "vouched"
                : "claimed";
            result.Add(new SkillDto(ps.Id, ps.SkillId, ps.Skill?.Name ?? "", ps.EvidenceText, status,
                best?.Score, best?.Total, vouchCount));
        }
        return result;
    }

    public async Task<ProfileDto> Build(Profile p) =>
        new(p.Id, p.Headline, p.Summary, p.IsPublic, p.ShareSlug, await BuildSkills(p.Id));
}
