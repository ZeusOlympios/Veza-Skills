# Quick end-to-end check of the API. Run from the repo root with the API running or deployed.
# Example:
#   .\scripts\smoke-test.ps1 -ApiUrl http://localhost:5000 -SupabaseUrl https://xxxx.supabase.co -AnonKey eyJ...
param(
  [Parameter(Mandatory=$true)][string]$ApiUrl,
  [Parameter(Mandatory=$true)][string]$SupabaseUrl,
  [Parameter(Mandatory=$true)][string]$AnonKey,
  [string]$Email = "demo@vezaskills.app",
  [string]$Password = "VezaDemo2026!"
)
$ErrorActionPreference = "Stop"
$ApiUrl = $ApiUrl.TrimEnd('/'); $SupabaseUrl = $SupabaseUrl.TrimEnd('/')
$authHeaders = @{ apikey = $AnonKey }
$creds = @{ email = $Email; password = $Password } | ConvertTo-Json

Write-Host "1. Sign in (or create) the demo user..."
try {
  $session = Invoke-RestMethod -Method Post -Uri "$SupabaseUrl/auth/v1/token?grant_type=password" -Headers $authHeaders -ContentType "application/json" -Body $creds
} catch {
  Write-Host "   No such user yet, signing up..."
  $session = Invoke-RestMethod -Method Post -Uri "$SupabaseUrl/auth/v1/signup" -Headers $authHeaders -ContentType "application/json" -Body $creds
}
if (-not $session.access_token) { throw "No access token returned. In Supabase turn OFF 'Confirm email' (Authentication > Providers > Email) and try again." }
$h = @{ Authorization = "Bearer $($session.access_token)" }

function Call($method, $path, $body = $null) {
  $args = @{ Method = $method; Uri = "$ApiUrl$path"; Headers = $h; ContentType = "application/json" }
  if ($body) { $args.Body = ($body | ConvertTo-Json -Depth 6) }
  Invoke-RestMethod @args
}

Write-Host "2. Health..."; (Invoke-RestMethod "$ApiUrl/api/health").status
Write-Host "3. /api/me..."; $me = Call Get "/api/me"; $me | ConvertTo-Json
Write-Host "4. Skills list..."; $skills = (Call Get "/api/skills").skills; "   $($skills.Count) skills loaded"
Write-Host "5. Chat (calls Claude)..."
$chat = Call Post "/api/chat" @{ message = "I help my aunt at her spaza shop after school. I serve customers and count the money at the end of the day."; language = "en" }
$chat | ConvertTo-Json -Depth 6
if ($chat.suggestedSkills.Count -gt 0) {
  $s = $chat.suggestedSkills[0]
  Write-Host "6. Add suggested skill '$($s.name)'..."
  Call Post "/api/profile/skills" @{ skillId = $s.skillId; evidenceText = $s.evidenceText } | ConvertTo-Json
  Write-Host "7. Generate CV..."; Call Post "/api/cv/generate" @{ language = "en" } | ConvertTo-Json -Depth 6
  Write-Host "8. Start quiz..."; $quiz = Call Post "/api/quiz/start" @{ skillId = $s.skillId; language = "en" }; "   attempt $($quiz.attemptId) with $($quiz.questions.Count) questions"
  $answers = @($quiz.questions | ForEach-Object { @{ questionId = $_.id; selectedIndex = 0 } })
  Write-Host "9. Submit quiz..."; Call Post "/api/quiz/$($quiz.attemptId)/submit" @{ answers = $answers } | ConvertTo-Json -Depth 6
  Write-Host "10. Vouch link..."; $v = Call Post "/api/vouches/requests" @{ refereeName = "Mr Dlamini"; relationship = "shop owner"; skillIds = @($s.skillId) }; $v | ConvertTo-Json
  Write-Host "11. Make profile public..."; $p = Call Put "/api/profile" @{ isPublic = $true }; "   public link slug: $($p.shareSlug)"
  Write-Host "12. Public profile..."; Invoke-RestMethod "$ApiUrl/api/public/profile/$($p.shareSlug)" | ConvertTo-Json -Depth 6
} else { Write-Host "No skills suggested; check the chat reply above." }
Write-Host "`nDone. The demo user is $Email / $Password"
