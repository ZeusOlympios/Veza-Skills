# VezaSkills: Data Model (v1)

Database: PostgreSQL on Supabase. Backend uses Entity Framework Core.
**Zeus owns migrations.** Nobody edits tables by hand in Supabase, so code and database cannot drift.
**Sanelisiwe owns seed data** (CSV files in `/data`, formats at the bottom).

Conventions: table and column names are `snake_case` in the database. IDs are UUIDs (never 1, 2, 3).
All timestamps are UTC (`timestamptz`). We store the minimum personal data: **no ID numbers**.

## Tables

### users
Mirrors the Supabase Auth user.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | Same as the Supabase Auth user id |
| display_name | text | |
| preferred_language | text | Language code, e.g. `en`, `zu` |
| role | text | `youth`, `employer`, `admin` (default `youth`) |
| created_at | timestamptz | |

### consents
POPIA: what the user agreed to and when.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| user_id | uuid FK users | |
| consent_type | text | `data_processing`, `ai_processing`, `public_profile` |
| granted_at | timestamptz | |
| revoked_at | timestamptz null | |

### profiles
One per user.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| user_id | uuid FK users, unique | |
| headline | text null | e.g. "Reliable shop assistant with tutoring experience" |
| summary | text null | AI-generated, user-edited |
| is_public | boolean | Default false |
| share_slug | text unique | Random string used in the public link |
| created_at / updated_at | timestamptz | |

### chat_messages
The skills conversation.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| profile_id | uuid FK profiles | |
| role | text | `user` or `assistant` |
| content | text | |
| language | text | |
| created_at | timestamptz | |

### skills
The skills list (seeded).
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name | text | e.g. "Customer service" |
| category | text | e.g. "People", "Practical", "Digital" |
| description | text | Plain language |

### profile_skills
A skill a user claims, with the evidence from their story.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| profile_id | uuid FK profiles | |
| skill_id | uuid FK skills | unique with profile_id |
| evidence_text | text | What they did, in their words |
| source | text | `ai_chat` or `manual` |
| confidence | numeric(3,2) null | AI confidence, 0 to 1 |
| status | text | `claimed`, `checked`, `vouched`, `verified` |
| created_at | timestamptz | |

Status rules: `checked` = passed the skills check. `vouched` = at least one vouch.
`verified` = both.

### quiz_questions
The question bank (seeded, can be extended with AI-generated questions).
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| skill_id | uuid FK skills | |
| language | text | |
| question | text | |
| options | jsonb | Array of 4 strings |
| correct_index | int | **Never sent to the front end** |
| explanation | text | |
| difficulty | int | 1 to 3 |

### quiz_attempts
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| profile_id | uuid FK profiles | |
| skill_id | uuid FK skills | |
| question_ids | jsonb | The questions served |
| answers | jsonb null | Filled on submit |
| score | int null | |
| total | int | |
| passed | boolean null | Pass mark: 60% |
| started_at / submitted_at | timestamptz | |

### vouch_requests
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| profile_id | uuid FK profiles | |
| token_hash | text unique | **We store only a hash.** The raw token appears once, in the link |
| referee_name | text | |
| relationship | text | e.g. "teacher", "shop owner" |
| skill_ids | jsonb | Skills the referee is asked about |
| status | text | `pending`, `completed`, `expired` |
| expires_at | timestamptz | 7 days after creation |
| created_at | timestamptz | |

### vouches
One per completed request.
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| vouch_request_id | uuid FK vouch_requests, unique | One vouch per link |
| profile_id | uuid FK profiles | |
| confirmed_skill_ids | jsonb | Skills the referee confirmed |
| comment | text null | Max 280 characters |
| created_at | timestamptz | |

### roles and role_skills (seeded, for role matching)
**roles:** `id`, `title`, `sector`, `description`, `entry_level` (boolean).
**role_skills:** `role_id`, `skill_id`, `weight` (1 to 3). Primary key is the pair.

## Relationships
- users 1:1 profiles
- profiles 1:many chat_messages, profile_skills, quiz_attempts, vouch_requests
- skills 1:many profile_skills, quiz_questions
- vouch_requests 1:1 vouches
- roles many:many skills (via role_skills)

## Indexes
`profiles.share_slug`, `vouch_requests.token_hash`, `profile_skills (profile_id, skill_id)`,
`quiz_questions (skill_id, language)`.

## Privacy rules
- No ID numbers, home addresses or financial data.
- Referee contact details are optional and not stored by default.
- Deleting a user removes their profile, chats, skills, attempts and vouch data.
- The public profile shows only: display name, headline, summary, skills with status, quiz scores and vouch counts.

## Seed file formats (Sanelisiwe, put these in /data)
- `skills.csv`: `name,category,description`
- `roles.csv`: `title,sector,description,entry_level`
- `role_skills.csv`: `role_title,skill_name,weight`
- `quiz_questions.csv`: `skill_name,language,question,option_a,option_b,option_c,option_d,correct_index,explanation,difficulty`

Use plain language and keep skill names identical across all files.
Start with about 25 skills, 10 entry-level roles, and 5 questions per skill in English.
Add other languages only if someone on the team can check them.
