# AGSA Static Site (Astro + Tailwind)

Static-first AGSA website with sponsorship funnel, Stripe Checkout integration (via Azure Function), and config-driven content.

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

## Sponsorship form routes

- `/sponsor` - sponsorship landing + tier selector + form + review
- `/sponsor/thanks` - success state after Stripe
- `/sponsor/cancel` - canceled payment state
- `/sponsor/upload-logo` - optional logo upload form

## Config-driven sponsorship data

Edit these files:

- `src/config/sponsorship-tiers.json`
  - `id, name, price, description, benefits[], isTeamQuantityAllowed, suggestedFor[], fulfillmentRequirements`
- `src/config/sponsorship-settings.json`
  - `processingFeeMode, flatFeeAmount, deadlineDate, mailingAddressForChecks, einTaxLanguage, ein, instagramHandle, instagramUrl, facebookUrl`

Also used:

- `src/config/site.json` for contact routing and global branding.

## Stripe setup (Azure Static Web Apps Function)

Function path:

- `api/createCheckoutSession`

Required server env var:

- `STRIPE_SECRET_KEY`

Optional if you move to fixed Stripe Prices:

- `STRIPE_PRICE_*` IDs (not required in current default implementation because line items are created with dynamic `price_data` from tier config).

Client calls:

- `POST /.auth/functions/createCheckoutSession`

SWA route rewrite is configured in:

- `staticwebapp.config.json`

## Submission / record keeping

Form payload submission endpoint is configured in:

- `src/config/sponsorship-settings.json` -> `submissionEndpoint`

Default is Formspree placeholder; replace with your live endpoint.

Logo upload endpoint:

- `src/config/sponsorship-settings.json` -> `logoUploadEndpoint`

## Azure Static Web Apps deployment

Workflow:

- `.github/workflows/azure-static-web-apps-jolly-dune-095686e0f.yml`

Configured for:

- `app_location: agsa-site`
- `api_location: agsa-site/api`
- `output_location: dist`

## Edit registration links

Single registration doorway data:

- `src/config/registration-links.json`

## Calendar setup

Use:

- `src/config/calendar.json`

Modes:

- `ics` (preferred)
- `gcal_api` (requires public referrer-restricted API key in env)

## Test end-to-end sponsorship flow

1. Open `/sponsor`.
2. Select a tier and complete required fields.
3. Review step should show subtotal + fee + total + benefits.
4. Click "Pay online with card".
5. Confirm redirect to Stripe Checkout session URL.
6. Complete or cancel payment to verify:
   - `/sponsor/thanks`
   - `/sponsor/cancel`
7. Test `/sponsor/upload-logo` with an `appId` query string.

## Accessibility notes

- Semantic form controls + labels.
- Keyboard reachable tier selection and action buttons.
- Visible focus states from shared global styles.
- Mobile sticky summary for sponsor flow.
