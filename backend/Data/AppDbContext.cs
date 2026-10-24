using Microsoft.EntityFrameworkCore;
using VezaSkills.Api.Models;

namespace VezaSkills.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<ProfileSkill> ProfileSkills => Set<ProfileSkill>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<VouchRequest> VouchRequests => Set<VouchRequest>();
    public DbSet<Vouch> Vouches => Set<Vouch>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<RoleSkill> RoleSkills => Set<RoleSkill>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Profile>().HasIndex(p => p.UserId).IsUnique();
        b.Entity<Profile>().HasIndex(p => p.ShareSlug).IsUnique();
        b.Entity<ProfileSkill>().HasIndex(p => new { p.ProfileId, p.SkillId }).IsUnique();
        b.Entity<ProfileSkill>().HasOne(p => p.Skill).WithMany().HasForeignKey(p => p.SkillId);
        b.Entity<QuizQuestion>().HasIndex(q => new { q.SkillId, q.Language });
        b.Entity<VouchRequest>().HasIndex(v => v.TokenHash).IsUnique();
        b.Entity<Vouch>().HasIndex(v => v.VouchRequestId).IsUnique();
        b.Entity<RoleSkill>().HasKey(r => new { r.RoleId, r.SkillId });
        b.Entity<Skill>().HasIndex(s => s.Name).IsUnique();
    }
}
