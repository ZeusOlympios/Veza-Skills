# VezaSkills: API Contract (v1)

**Zeus builds these endpoints. Khululekani builds screens against the example JSON below.**
If something must change, edit this file on a branch and tell the group in one line.

## Basics
- Base path: `/api`. JSON in and out, `camelCase` field names.
- Auth: `Authorization: Bearer <Supabase access token>` on every endpoint except those marked **public**.
- Errors always look like this:
```json
{ "error": { "code": "validation_failed", "message": "Message is too long." } }
```
- Status codes: `400` invalid input, `401` not logged in, `403` not allowed, `404` not found, `429` too many requests.
- Language codes: `en`, `zu`, `xh`, `st`, `tn`, `af`. The app only offers languages we have tested.
- Skill status values: `claimed`, `checked`, `vouched`, `verified`.

## Health (public)
`GET /api/health` returns `{ "status": "ok" }`

## Account
`GET /api/me` returns:
```json
{ "id": "uuid", "displayName": "Thandi", "preferredLanguage": "zu", "role": "youth" }
```
`PUT /api/me` with `{ "displayName": "Thandi", "preferredLanguage": "zu" }` returns the same shape.
`POST /api/me/consents` with `{ "type": "ai_processing" }` returns `{ "ok": true }`.
`DELETE /api/me` deletes the account and all data, and returns `{ "ok": true }`.

## Profile
`GET /api/profile` returns (created on first call):
```json
{
  "id": "uuid",
  "headline": "Reliable shop assistant with tutoring experience",
  "summary": "Thandi has helped run a spaza shop and tutored younger learners...",
  "isPublic": false,
  "shareSlug": "k8f2x9q",
  "skills": [
    { "id": "uuid", "skillId": "uuid", "name": "Customer service",
      "evidenceText": "I served customers and handled cash at my aunt's shop",
      "status": "checked", "quizScore": 4, "quizTotal": 5, "vouchCount": 0 }
  ]
}
```
`PUT /api/profile` with `{ "headline": "...", "summary": "...", "isPublic": true }` returns the same shape.

## Chat: skills discovery
`POST /api/chat` (rate limit: 30 messages per hour per user)
```json
{ "message": "Ngisize esitolo sikamama wami...", "language": "zu" }
```
Returns:
```json
{
  "reply": "Ngiyabonga! Ucele kanjani amakhasimende...?",
  "suggestedSkills": [
    { "skillId": "uuid", "name": "Customer service", "evidenceText": "Helped customers at the shop", "confidence": 0.82 }
  ]
}
```
The front end shows `suggestedSkills` as cards the user accepts or rejects.
`GET /api/chat/history` returns `{ "messages": [ { "role": "user", "content": "...", "createdAt": "..." } ] }`.

## Skills
`GET /api/skills` returns `{ "skills": [ { "id": "uuid", "name": "Customer service", "category": "People", "description": "..." } ] }`.
`POST /api/profile/skills` with `{ "skillId": "uuid", "evidenceText": "..." }` returns the new profile skill.
`DELETE /api/profile/skills/{id}` returns `{ "ok": true }`.

## CV data
`GET /api/cv` returns the data the templates render. The front end builds the CV from this and exports PDF by printing the page.
```json
{
  "fullName": "Thandi Nkosi",
  "headline": "Reliable shop assistant with tutoring experience",
  "summary": "...",
  "language": "en",
  "skills": [ { "name": "Customer service", "bullets": ["Served customers and handled cash at a family shop"], "status": "verified" } ]
}
```
`POST /api/cv/generate` (rate limit: 10 per hour) asks the AI to rewrite the headline, summary and bullets from the user's skills and returns the same shape. The user edits it before saving.

## Skills check
`POST /api/quiz/start` with `{ "skillId": "uuid", "language": "en" }` returns:
```json
{
  "attemptId": "uuid",
  "questions": [ { "id": "uuid", "question": "A customer is unhappy...", "options": ["A", "B", "C", "D"] } ]
}
```
Correct answers are never included.
`POST /api/quiz/{attemptId}/submit` with `{ "answers": [ { "questionId": "uuid", "selectedIndex": 2 } ] }` returns:
```json
{ "score": 4, "total": 5, "passed": true,
  "results": [ { "questionId": "uuid", "correct": true, "explanation": "..." } ] }
```
`GET /api/quiz/results` returns the user's attempts.

## Vouching
`POST /api/vouches/requests` with `{ "refereeName": "Mr Dlamini", "relationship": "shop owner", "skillIds": ["uuid"] }` returns:
```json
{ "requestId": "uuid", "vouchUrl": "https://<frontend>/vouch/<token>", "expiresAt": "..." }
```
The link is shown once. The user shares it on WhatsApp.

`GET /api/vouches/public/{token}` (**public**) returns:
```json
{ "candidateFirstName": "Thandi", "relationship": "shop owner",
  "skills": [ { "id": "uuid", "name": "Customer service" } ] }
```
`POST /api/vouches/public/{token}` (**public**, one use) with `{ "confirmedSkillIds": ["uuid"], "comment": "Reliable and friendly." }` returns `{ "ok": true }`.
Used, expired or unknown tokens return `404` with the same message each time.
`GET /api/vouches` returns the user's requests and whether each was completed.

## Verified profile (public)
`GET /api/public/profile/{shareSlug}` (**public**, only works if `isPublic` is true) returns:
```json
{
  "displayName": "Thandi N.",
  "headline": "...",
  "summary": "...",
  "skills": [ { "name": "Customer service", "status": "verified", "quizScore": 4, "quizTotal": 5, "vouchCount": 2 } ],
  "updatedAt": "..."
}
```
The QR code is drawn on the front end from `https://<frontend>/p/{shareSlug}`.

## Role matching (stretch goal)
`GET /api/matches` returns:
```json
{ "matches": [ { "roleId": "uuid", "title": "Retail assistant", "matchPercent": 78, "missingSkills": ["Cash handling"] } ] }
```

## Security notes (also for the SSDLC)
- The Claude API key exists only on the server. The front end never calls the AI directly.
- Every endpoint checks the logged-in user owns the record it touches.
- Chat and generate endpoints are rate limited. Input has maximum lengths (chat message 1,000 characters).
- CORS allows only the deployed front-end address.
- Public endpoints return only the fields shown above.

## Open decisions
- Which languages are supported on day one (tested by a teammate)?
- Do we show a vouch from an unverified referee as "vouched" or "vouched (unverified)"? Default: "vouched" with the relationship shown.
