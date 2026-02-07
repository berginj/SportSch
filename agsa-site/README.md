# AGSA Static Website (Astro + Tailwind)

Static-first AGSA site with config-driven sponsorship packaging, Stripe Checkout via Azure Functions, and Instagram-first sponsor communications.

## Local development

From `agsa-site/`:

```bash
npm install
npm run dev
```

Build:

```bash
npm run build
```

## Action image extraction pipeline

Tooling script:

- `tools/extract_site_images.py`

Install dependencies:

```bash
pip install requests beautifulsoup4 pillow imagehash
```

Run from `agsa-site/`:

```bash
python tools/extract_site_images.py \
  --base-url https://agsafastpitch.com \
  --max-pages 200 \
  --max-depth 3 \
  --rate-limit-ms 800 \
  --output-dir ./public/images/candidates \
  --allow-domains agsafastpitch.com,cdn2.sportngin.com
```

Outputs:

- `public/images/candidates/manifest.json` (all candidate images)
- `public/images/candidates/review.html` (local approval UI)
- `public/images/candidates/approved-manifest.json` (approved-only list for website use)

Review workflow:

1. Open `public/images/candidates/review.html` in your browser.
2. Mark items as approved / needs rights check / reject.
3. Click export to download `approved-manifest.json`.
4. Replace `public/images/candidates/approved-manifest.json` with the exported file.
5. Rebuild the site (`npm run build`).

Legal/ethical guardrails:

- Crawl public pages only, no auth bypass.
- Script respects `robots.txt`.
- Do not identify or label minors.
- Human rights/consent review is required before publishing images.

Homepage integration:

- `src/components/ApprovedActionGallery.astro` reads `approved-manifest.json`.
- Only approved images are used in the hero/moments/highlights photo sections.

## Sponsorship routes

- `/sponsor` main sponsorship workflow (Choose Tier -> Sponsor Info -> Review -> Pay)
- `/sponsor/thanks` Stripe success state
- `/sponsor/cancel` Stripe cancellation state
- `/sponsor/check` pay-by-check instructions + printable summary
- `/sponsor/upload-logo` logo upload page (Tier 4 only)
- `/sponsors` public sponsor listing (featured + team/division groups)

## Config files

Edit only config/content files; avoid component edits for content updates.

- `src/config/sponsorship-settings.json`
  - Org/tax fields (`orgName`, `ein`, `taxLanguage`)
  - `sponsorshipTermMonths` (12)
  - `deadlineDate`
  - `bannerLocations`
  - social links (`instagramHandle`, `instagramUrl`, optional `facebookUrl`)
  - contact emails and payment mode settings
  - division dropdown options for Tier 4
- `src/config/sponsorship-tiers.json`
  - Four enforced tiers and benefits
  - pricing logic (`pricingMode`, `basePrice`, `multiTeamPricing`)
  - `jerseyLogoAllowed` and `barcroftBannerIncluded`
- `src/config/sponsors.json`
  - Featured tiers at top (Tier 3/4 style)
  - Team/division sponsor name groupings
- `src/config/registration-links.json`
  - Single registration doorway links
- `src/config/calendar.json`
  - Schedule source mode (`ics` preferred or `gcal_api`)

## Stripe + submission function setup

Functions live in `agsa-site/api`.

### Required environment variables

- `STRIPE_SECRET_KEY` (required for `/api/createCheckoutSession`)

### Recommended environment variables

- `SENDGRID_API_KEY` (for `/api/submitSponsorship` email)
- `SPONSOR_EMAIL_FROM` (verified sender)
- `SPONSOR_NOTIFICATION_EMAILS` (comma-separated recipients; defaults to `sponsors@agsafastpitch.com,marketing@agsafastpitch.com`)

### Function endpoints

- `POST /api/submitSponsorship`
  - Creates/returns `applicationId`
  - Sends sponsorship details email when SendGrid vars are configured
- `POST /api/createCheckoutSession`
  - Creates Stripe Checkout session from server-side tier pricing
  - Includes metadata: `applicationId`, sponsor fields, tier, quantity/division

## Azure Static Web Apps deployment

Workflow:

- `.github/workflows/azure-static-web-apps-jolly-dune-095686e0f.yml`

Configured:

- `app_location: agsa-site`
- `api_location: agsa-site/api`
- `output_location: dist`

## End-to-end test checklist

1. Open `/sponsor`.
2. Select each tier and confirm business rules:
   - Tier 1: team count >= 1
   - Tier 2: team count only 2/3/4
   - Tier 4: division selection required, logo field visible
3. Continue through all 4 steps and verify summary:
   - 1-year term displayed
   - Banner inclusion shown for Tier 3/4
   - Jersey logo shown only for Tier 4
4. Use card flow:
   - Submit -> Stripe checkout redirect works
   - Success -> `/sponsor/thanks` shows term + tax language + next steps
5. Use check flow:
   - Submit -> `/sponsor/check?appId=...` shows printable instructions
6. Verify `/sponsors` lists featured tiers first and team/division names below.

## Ops notes (internal)

- Tier 4 pricing rationale (not public UI):
  - Imprinting basis: `$5 per jersey x 10 teams x 15 uniforms = $750` (8 teams = $600).
  - Tier 4 remains mission-positive funding beyond imprinting costs.
- Keep Instagram as primary sponsor channel in copy and CTAs.
- Keep Facebook as footer/contact link only.
