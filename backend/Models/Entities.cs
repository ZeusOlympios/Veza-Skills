namespace VezaSkills.Api.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string PreferredLanguage { get; set; } = "en";
    public string Role { get; set; } = "youth";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Consent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = "";
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

public class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public bool IsPublic { get; set; }
    public string ShareSlug { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string Language { get; set; } = "en";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Skill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ProfileSkill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid SkillId { get; set; }
    public string EvidenceText { get; set; } = "";
    public string Source { get; set; } = "manual";
    public double? Confidence { get; set; }
    // JSON array of strings (CV bullets), null until generated
    public string? BulletsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Skill? Skill { get; set; }
}

public class QuizQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SkillId { get; set; }
    public string Language { get; set; } = "en";
    public string Question { get; set; } = "";
    // JSON array of 4 strings
    public string OptionsJson { get; set; } = "[]";
    public int CorrectIndex { get; set; }
    public string Explanation { get; set; } = "";
    public int Difficulty { get; set; } = 1;
}

public class QuizAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid SkillId { get; set; }
    // JSON array of question ids
    public string QuestionIdsJson { get; set; } = "[]";
    // JSON array of {questionId, selectedIndex}
    public string? AnswersJson { get; set; }
    public int? Score { get; set; }
    public int Total { get; set; }
    public bool? Passed { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
}

public class VouchRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string TokenHash { get; set; } = "";
    public string RefereeName { get; set; } = "";
    public string Relationship { get; set; } = "";
    // JSON array of skill ids
    public string SkillIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "pending";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Vouch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VouchRequestId { get; set; }
    public Guid ProfileId { get; set; }
    // JSON array of skill ids
    public string ConfirmedSkillIdsJson { get; set; } = "[]";
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class JobRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Sector { get; set; } = "";
    public string Description { get; set; } = "";
    public bool EntryLevel { get; set; } = true;
}

public class RoleSkill
{
    public Guid RoleId { get; set; }
    public Guid SkillId { get; set; }
    public int Weight { get; set; } = 1;
}
