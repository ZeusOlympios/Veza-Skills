# VezaSkills frontend

React + TypeScript interface for the VezaSkills API. The landing page follows `websitse-template.html`; the signed-in workspace is connected to the API contract in `../docs/api-contract.md`.

## Run locally against the deployed API

1. Make sure `.env.local` contains `VITE_API_URL`, `VITE_SUPABASE_URL`, and `VITE_SUPABASE_ANON_KEY`.
2. From this folder, run `npm install` and `npm run dev`.
3. Open the local URL Vite prints. Create an account or log in from the landing page. Access and refresh tokens are stored in the current browser session.

The Supabase Auth URL configuration must allow the local development URL and deployed frontend URL as redirect URLs. The API CORS `FRONTEND_URL` setting must also allow each frontend origin. The backend’s Supabase URL and publishable/anon key must be configured in its hosting environment for token validation.

## Local backend

Start the API with `dotnet run --project ../backend/VezaSkills.Api.csproj`; its development URL is `http://localhost:5221`. To use it instead of the hosted API, set `VITE_API_URL` to `http://localhost:5221` in `.env.local`. Vite proxies `/api` to the local API when using the default relative URL.

## Features

Responsive landing page and touch-friendly feature carousel, Supabase email/password sign-up and login, password recovery and password update, private skills passport, conversational skill discovery, searchable skills library, quiz checks, job matches, guided CV form with optional personal details, four templates, profile photo upload, Word and PDF downloads, a job-description quiz and truthful CV tailoring, referee requests and WhatsApp sharing, public profile, and one-time public vouch pages. Public routes are `/p/{shareSlug}` and `/vouch/{token}`.

For production hosting, set the three `VITE_*` environment variables in the frontend build environment. Vite embeds them into the client bundle; only the Supabase publishable key belongs there, never a service-role key.



CV questionnaire details and photo are stored in the current browser session. The API-saved profile headline, summary and passport skill bullets are separate. Word export downloads a Word-compatible .doc file; PDF export opens the browser print dialog, where the user can choose Save as PDF.

