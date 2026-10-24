using System.Security.Cryptography;
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
[Route("api/vouches")]
public class VouchController : ApiBase
{
    public VouchController(AppDbContext db) : base(db) { }

    public record CreateVouchReq(string? RefereeName, string? Relationship, List<Guid>? SkillIds);
    public record SubmitVouchReq(List<Guid>? ConfirmedSkillIds, string? Comment);

    private const string InvalidLink = "This link is not valid or has expired.";

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FrontendBase()
    {
        var pub = Environment.GetEnvironmentVariable("PUBLIC_FRONTEND_URL");
        if (string.IsNullOrWhiteSpace(pub))
            pub = (Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:5173")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return (pub ?? "http://localhost:5173").TrimEnd('/');
    }

    private async Task<VouchRequest?> FindValid(string token)
    {
        if (token.Length < 10 || token.Length > 100) return null;
        var h = Hash(token);
        var vr = await Db.VouchRequests.FirstOrDefaultAsync(v => v.TokenHash == h);
        if (vr == null || vr.Status != "pending" || vr.ExpiresAt < DateTime.UtcNow) return null;
        return vr;
    }

    // ---- Logged-in user ----

    [HttpPost("requests")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateVouchReq req)
    {
        var (_, profile) = await EnsureUserAsync();
        var name = req.RefereeName?.Trim() ?? "";
        var rel = req.Relationship?.Trim() ?? "";
        if (name.Length < 1 || name.Length > 80) return Err(400, "validation_failed", "Referee name must be 1 to 80 characters.");
        if (rel.Length < 1 || rel.Length > 60) return Err(400, "validation_failed", "Relationship must be 1 to 60 characters.");
        var skillIds = (req.SkillIds ?? new List<Guid>()).Distinct().ToList();
        if (skillIds.Count < 1 || skillIds.Count > 10) return Err(400, "validation_failed", "Choose 1 to 10 skills.");

        var owned = await Db.ProfileSkills.Where(p => p.ProfileId == profile.Id && skillIds.Contains(p.SkillId))
            .Select(p => p.SkillId).ToListAsync();
        if (owned.Count != skillIds.Count) return Err(400, "validation_failed", "Some skills are not on your profile.");

        var token = NewToken();
        var vr = new VouchRequest
        {
            ProfileId = profile.Id,
            TokenHash = Hash(token),
            RefereeName = name,
            Relationship = rel,
            SkillIdsJson = JsonSerializer.Serialize(skillIds),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        Db.VouchRequests.Add(vr);
        await Db.SaveChangesAsync();

        // The raw token is only ever returned here. Only its hash is stored.
        return Ok(new { requestId = vr.Id, vouchUrl = FrontendBase() + "/vouch/" + token, expiresAt = vr.ExpiresAt });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List()
    {
        var (_, profile) = await EnsureUserAsync();
        var items = await Db.VouchRequests.Where(v => v.ProfileId == profile.Id)
            .OrderByDescending(v => v.CreatedAt).ToListAsync();
        var requests = items.Select(v => new
        {
            id = v.Id,
            refereeName = v.RefereeName,
            relationship = v.Relationship,
            status = v.Status == "pending" && v.ExpiresAt < DateTime.UtcNow ? "expired" : v.Status,
            expiresAt = v.ExpiresAt,
            createdAt = v.CreatedAt
        });
        return Ok(new { requests });
    }

    // ---- Public (the referee, no account) ----

    [HttpGet("public/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> PublicGet(string token)
    {
        var vr = await FindValid(token);
        if (vr == null) return Err(404, "not_found", InvalidLink);

        var profile = await Db.Profiles.FirstOrDefaultAsync(p => p.Id == vr.ProfileId);
        var owner = profile == null ? null : await Db.Users.FindAsync(profile.UserId);
        var first = (owner?.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        var ids = ProfileService.ParseIds(vr.SkillIdsJson);
        var skills = await Db.Skills.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToListAsync();
        return Ok(new { candidateFirstName = first, relationship = vr.Relationship, skills });
    }

    [HttpPost("public/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> PublicPost(string token, [FromBody] SubmitVouchReq req)
    {
        var vr = await FindValid(token);
        if (vr == null) return Err(404, "not_found", InvalidLink);

        var allowed = ProfileService.ParseIds(vr.SkillIdsJson);
        var confirmed = (req.ConfirmedSkillIds ?? new List<Guid>()).Distinct().ToList();
        if (confirmed.Count == 0 || confirmed.Any(c => !allowed.Contains(c)))
            return Err(400, "validation_failed", "Choose at least one of the listed skills.");
        var comment = req.Comment?.Trim();
        if (comment != null && comment.Length > 280) return Err(400, "validation_failed", "Comment is too long (max 280).");

        try
        {
            Db.Vouches.Add(new Vouch
            {
                VouchRequestId = vr.Id,
                ProfileId = vr.ProfileId,
                ConfirmedSkillIdsJson = JsonSerializer.Serialize(confirmed),
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment
            });
            vr.Status = "completed";
            await Db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Already used (unique index on the request)
            return Err(404, "not_found", InvalidLink);
        }
        return Ok(new { ok = true });
    }
}

[ApiController]
[Route("api/public/profile")]
public class PublicProfileController : ApiBase
{
    private readonly ProfileService _profiles;
    public PublicProfileController(AppDbContext db, ProfileService profiles) : base(db) { _profiles = profiles; }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> Get(string slug)
    {
        if (slug.Length < 4 || slug.Length > 40) return Err(404, "not_found", "Profile not found.");
        var p = await Db.Profiles.FirstOrDefaultAsync(x => x.ShareSlug == slug && x.IsPublic);
        if (p == null) return Err(404, "not_found", "Profile not found.");

        var owner = await Db.Users.FindAsync(p.UserId);
        var parts = (owner?.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var displayName = parts.Length == 0 ? "Job seeker"
            : parts.Length == 1 ? parts[0]
            : parts[0] + " " + char.ToUpperInvariant(parts[^1][0]) + ".";

        var skills = (await _profiles.BuildSkills(p.Id)).Select(s => new
        {
            name = s.Name,
            status = s.Status,
            quizScore = s.QuizScore,
            quizTotal = s.QuizTotal,
            vouchCount = s.VouchCount
        });
        return Ok(new { displayName, headline = p.Headline, summary = p.Summary, skills, updatedAt = p.UpdatedAt });
    }
}
