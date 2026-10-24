using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Data;
using VezaSkills.Api.Models;
using VezaSkills.Api.Services;

namespace VezaSkills.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ApiBase
{
    public MeController(AppDbContext db) : base(db) { }

    public record MeDto(Guid Id, string DisplayName, string PreferredLanguage, string Role);
    public record UpdateMeReq(string? DisplayName, string? PreferredLanguage);
    public record ConsentReq(string? Type);

    private static readonly HashSet<string> ConsentTypes = new() { "data_processing", "ai_processing", "public_profile" };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (u, _) = await EnsureUserAsync();
        return Ok(new MeDto(u.Id, u.DisplayName, u.PreferredLanguage, u.Role));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdateMeReq req)
    {
        var (u, _) = await EnsureUserAsync();
        if (req.DisplayName != null)
        {
            var n = req.DisplayName.Trim();
            if (n.Length < 1 || n.Length > 60) return Err(400, "validation_failed", "Name must be 1 to 60 characters.");
            u.DisplayName = n;
        }
        if (req.PreferredLanguage != null)
        {
            var l = req.PreferredLanguage.Trim().ToLowerInvariant();
            if (!Languages.Contains(l)) return Err(400, "validation_failed", "Unsupported language.");
            u.PreferredLanguage = l;
        }
        await Db.SaveChangesAsync();
        return Ok(new MeDto(u.Id, u.DisplayName, u.PreferredLanguage, u.Role));
    }

    [HttpPost("consents")]
    public async Task<IActionResult> AddConsent([FromBody] ConsentReq req)
    {
        var (u, _) = await EnsureUserAsync();
        var type = req.Type?.Trim() ?? "";
        if (!ConsentTypes.Contains(type)) return Err(400, "validation_failed", "Unknown consent type.");
        var exists = await Db.Consents.AnyAsync(c => c.UserId == u.Id && c.ConsentType == type && c.RevokedAt == null);
        if (!exists) Db.Consents.Add(new Consent { UserId = u.Id, ConsentType = type });
        await Db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // Deletes all of the user's app data (POPIA). The Supabase login record is removed separately.
    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        var id = UserId;
        var profile = await Db.Profiles.FirstOrDefaultAsync(p => p.UserId == id);
        if (profile != null)
        {
            var pid = profile.Id;
            await Db.ChatMessages.Where(c => c.ProfileId == pid).ExecuteDeleteAsync();
            await Db.ProfileSkills.Where(c => c.ProfileId == pid).ExecuteDeleteAsync();
            await Db.QuizAttempts.Where(c => c.ProfileId == pid).ExecuteDeleteAsync();
            await Db.Vouches.Where(c => c.ProfileId == pid).ExecuteDeleteAsync();
            await Db.VouchRequests.Where(c => c.ProfileId == pid).ExecuteDeleteAsync();
            await Db.Profiles.Where(c => c.Id == pid).ExecuteDeleteAsync();
        }
        await Db.Consents.Where(c => c.UserId == id).ExecuteDeleteAsync();
        await Db.Users.Where(u => u.Id == id).ExecuteDeleteAsync();
        return Ok(new { ok = true });
    }
}

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ApiBase
{
    private readonly ProfileService _profiles;
    public ProfileController(AppDbContext db, ProfileService profiles) : base(db) { _profiles = profiles; }

    public record UpdateProfileReq(string? Headline, string? Summary, bool? IsPublic);
    public record AddSkillReq(Guid SkillId, string? EvidenceText);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (_, p) = await EnsureUserAsync();
        return Ok(await _profiles.Build(p));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdateProfileReq req)
    {
        var (_, p) = await EnsureUserAsync();
        if (req.Headline != null)
        {
            if (req.Headline.Length > 120) return Err(400, "validation_failed", "Headline is too long (max 120).");
            p.Headline = req.Headline.Trim();
        }
        if (req.Summary != null)
        {
            if (req.Summary.Length > 1000) return Err(400, "validation_failed", "Summary is too long (max 1000).");
            p.Summary = req.Summary.Trim();
        }
        if (req.IsPublic.HasValue) p.IsPublic = req.IsPublic.Value;
        p.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return Ok(await _profiles.Build(p));
    }

    [HttpPost("skills")]
    public async Task<IActionResult> AddSkill([FromBody] AddSkillReq req)
    {
        var (_, p) = await EnsureUserAsync();
        var evidence = req.EvidenceText?.Trim() ?? "";
        if (evidence.Length > 500) return Err(400, "validation_failed", "Evidence is too long (max 500).");
        if (!await Db.Skills.AnyAsync(s => s.Id == req.SkillId)) return Err(404, "not_found", "Skill not found.");

        var existing = await Db.ProfileSkills.FirstOrDefaultAsync(x => x.ProfileId == p.Id && x.SkillId == req.SkillId);
        if (existing == null)
        {
            Db.ProfileSkills.Add(new ProfileSkill { ProfileId = p.Id, SkillId = req.SkillId, EvidenceText = evidence, Source = "manual" });
        }
        else if (evidence.Length > 0)
        {
            existing.EvidenceText = evidence;
        }
        p.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        var skills = await _profiles.BuildSkills(p.Id);
        return Ok(skills.First(s => s.SkillId == req.SkillId));
    }

    [HttpDelete("skills/{id:guid}")]
    public async Task<IActionResult> RemoveSkill(Guid id)
    {
        var (_, p) = await EnsureUserAsync();
        var deleted = await Db.ProfileSkills.Where(x => x.Id == id && x.ProfileId == p.Id).ExecuteDeleteAsync();
        if (deleted == 0) return Err(404, "not_found", "Skill not found on your profile.");
        return Ok(new { ok = true });
    }
}

[ApiController]
[Route("api/skills")]
[Authorize]
public class SkillsController : ApiBase
{
    public SkillsController(AppDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var skills = await Db.Skills.OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Category, s.Description }).ToListAsync();
        return Ok(new { skills });
    }
}
