# AGSA Fastpitch Static Website (Astro)

Modern static website scaffold for AGSA Fastpitch built with Astro + Tailwind.

## Local Development

1. Install dependencies:
   ```bash
   npm install
   ```
2. Start dev server:
   ```bash
   npm run dev
   ```
3. Build production output:
   ```bash
   npm run build
   ```
4. Preview build:
   ```bash
   npm run preview
   ```

## Config-Driven Content

All core content is editable without touching components.

- `src/config/site.json`
  - Organization name, contact details, socials, quick actions, announcement banner, get-involved cards.
- `src/config/theme.json`
  - Theme tokens (colors/fonts/logo paths) used across layout and Tailwind config.
- `src/config/registration-links.json`
  - Centralized registration CTA links. Replace placeholder URLs with SportsEngine URLs.
- `src/config/calendar.json`
  - Calendar mode and data source settings.

Additional content:

- News posts: `src/content/news/*.md`
- Sponsor tiers and logos: `src/config/sponsors.json` + files in `public/images/sponsors/`

## Registration URL Replacement (SportsEngine)

Replace all placeholder URLs in:

- `src/config/registration-links.json`

Optionally add SportsEngine links for other flows (team pages, standings, rosters) by adding new buttons/entries in config and rendering them in relevant pages.

## Google Calendar Integration

Two supported modes via `src/config/calendar.json`:

1. `mode: "ics"` (default)
   - Set `icsUrl` to a public ICS feed.
   - Client fetches + parses ICS and caches in `localStorage` for 20 minutes.
   - If CORS blocks feed access, switch to `gcal_api` or use a CORS-friendly feed.

2. `mode: "gcal_api"`
   - Set `calendarId`.
   - Set `apiKeyEnvVarName` (default: `PUBLIC_GCAL_API_KEY`).
   - Add key in `.env`:
     ```env
     PUBLIC_GCAL_API_KEY=your_public_key
     ```
   - Restrict key by HTTP referrer in Google Cloud Console.

## Social Integrations

- Facebook Page Plugin embed via iframe.
- Instagram embed blockquote + official embed script.
- Fallback follow links included for script/privacy blockers.

## Contact Form

`/contact` is configured for Formspree:

- Update form action in `src/pages/contact.astro`:
  - `https://formspree.io/f/PLACEHOLDER_FORM_ID`

Fallback contact links (email/social) are always visible.

## Deployment

### Netlify

Build settings:

- Base directory: `agsa-site`
- Build command: `npm run build`
- Publish directory: `dist`

### Cloudflare Pages

Build settings:

- Root directory: `agsa-site`
- Build command: `npm run build`
- Build output directory: `dist`

### GitHub Pages

Use any Astro static deployment workflow. Publish `agsa-site/dist`.

## Accessibility + SEO Notes

- Semantic layout with landmark regions.
- Skip link and focus-visible states.
- Color contrast and keyboard-friendly controls.
- OG/Twitter metadata in shared layout.

## Brand Asset Notes

- Placeholder logo files are in `public/images/`.
- Replace with final AGSA brand assets when available:
  - `logo-placeholder-light.svg`
  - `logo-placeholder-dark.svg`
  - `logo-placeholder-mark.svg`
- If a full favicon set is needed, generate from final mark and place in `public/`.

## License / Assets

All bundled visual assets are placeholders created for scaffold purposes.
