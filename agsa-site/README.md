# AGSA Fastpitch Website (Static Astro)

Production-ready static website scaffold for Arlington Girls Softball Association.

## Run locally

```bash
npm install
npm run dev
```

Build:

```bash
npm run build
```

Preview:

```bash
npm run preview
```

## Core config files (admin-editable)

All key links/content should be edited in config/content files, not components.

- `src/config/site.json`
  - Brand copy, location, contact routing emails, socials, Instagram handle, partner links, homepage metadata.
- `src/config/theme.json`
  - Theme tokens and live AGSA logo paths.
- `src/config/registration-links.json`
  - Single registration doorway links by season/program (`status`, `priority`, optional highlight dates).
- `src/config/calendar.json`
  - Calendar source mode and settings (`ics` or `gcal_api`).
- `src/config/announcements.json`
  - Homepage banner items with `startDate`/`endDate` expiry.
- `src/config/sponsors.json`
  - Sponsor tiers and logo references.

Policy content is Markdown:

- `src/content/policies/*.md`

News content is Markdown:

- `src/content/news/*.md`

## Registration updates

Use only:

- `src/config/registration-links.json`

The `/register` page is the single registration router page for all programs/seasons.

## Schedule integration

Two modes in `src/config/calendar.json`:

1. `mode: "ics"` (preferred)
   - Set `icsUrl` to a public ICS feed.
   - Client parses ICS and caches data in localStorage.
2. `mode: "gcal_api"`
   - Set `calendarId`
   - Set `apiKeyEnvVarName` (default: `PUBLIC_GCAL_API_KEY`)
   - Add env var in deployment/local `.env`

Example `.env`:

```env
PUBLIC_GCAL_API_KEY=your_public_referrer_restricted_key
```

Important:

- Do not commit private keys.
- If using Google API client-side, treat key as public and restrict by referrer.

## Social integration

- Instagram is primary:
  - homepage Instagram embed + fallback follow CTA.
- Facebook is link-only:
  - footer/contact links (no Facebook feed embed).

## Forms (static-friendly)

Current pages use Formspree placeholders:

- Contact: `src/pages/contact.astro`
- Volunteer: `src/pages/get-involved.astro`
- Sponsor: `src/pages/sponsors.astro`

Replace `PLACEHOLDER_*` form IDs with real Formspree endpoints.

Spam mitigation:

- Honeypot field (`_gotcha`) already included.

## Governance features already implemented

- Announcement expiry using date windows (`announcements.json`).
- `Last updated` labels on key pages from config/content dates.
- Policies rendered as HTML from Markdown (not PDF-dependent).

## Deployment

### Azure Static Web Apps

Workflow is configured to deploy this app from:

- `app_location: "agsa-site"`
- `output_location: "dist"`

### Netlify

- Base directory: `agsa-site`
- Build command: `npm run build`
- Publish directory: `dist`

### Cloudflare Pages

- Root directory: `agsa-site`
- Build command: `npm run build`
- Build output directory: `dist`

## Assets

- AGSA logos and selected imagery are sourced from official AGSA web/Instagram content and stored in `public/images/agsa/`.
