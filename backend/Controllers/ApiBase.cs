using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Data;
using VezaSkills.Api.Models;

namespace VezaSkills.Api.Controllers;

public abstract class ApiBase : ControllerBase
{
    protected readonly AppDbContext Db;
    protected ApiBase(AppDbContext db) { Db = db; }

    protected static readonly HashSet<string> Languages = new() { "en", "zu", "xh", "st", "tn", "af" };

    protected static string NormLang(string? l) =>
        l != null && Languages.Contains(l.Trim().ToLowerInvariant()) ? l.Trim().ToLowerInvariant() : "en";

    protected IActionResult Err(int status, string code, string message) =>
        StatusCode(status, new { error = new { code, message } });

    protected static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Makes sure the logged-in user has a user row and a profile row
    protected async Task<(AppUser user, Profile profile)> EnsureUserAsync()
    {
        var id = UserId;
        var user = await Db.Users.FindAsync(id);
        var profile = await Db.Profiles.FirstOrDefaultAsync(p => p.UserId == id);
        if (user != null && profile != null) return (user, profile);

        try
        {
            if (user == null)
            {
                var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
                var name = email.Contains('@') ? email.Split('@')[0] : "Friend";
                user = new AppUser { Id = id, DisplayName = name.Length > 60 ? name[..60] : name };
                Db.Users.Add(user);
            }
            if (profile == null)
            {
                profile = new Profile
                {
                    UserId = id,
                    ShareSlug = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()
                };
                Db.Profiles.Add(profile);
            }
            await Db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two first requests at the same time: someone else created the rows, so read them
            Db.ChangeTracker.Clear();
            user = await Db.Users.FirstAsync(u => u.Id == id);
            profile = await Db.Profiles.FirstAsync(p => p.UserId == id);
        }
        return (user!, profile!);
    }
}
