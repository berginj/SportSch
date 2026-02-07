const Stripe = require("stripe");

const PRICE_BOOK = {
  "community-team": ({ teamCount }) => ({ unitAmount: 65000, quantity: teamCount, label: "Community Team Sponsor (per team)" }),
  "multi-team": ({ teamCount }) => {
    const table = { 2: 120000, 3: 175000, 4: 225000 };
    const total = table[teamCount];
    if (!total) return null;
    return { unitAmount: total, quantity: 1, label: `Multi-Team Sponsor (${teamCount} teams)` };
  },
  "league-partner": () => ({ unitAmount: 500000, quantity: 1, label: "League Partner" }),
  "division-logo": () => ({ unitAmount: 850000, quantity: 1, label: "Division Logo Sponsor" })
};

function toSafeString(input) {
  return String(input || "").trim();
}

module.exports = async function (context, req) {
  try {
    const secret = process.env.STRIPE_SECRET_KEY;
    if (!secret) {
      context.res = { status: 500, body: "Missing STRIPE_SECRET_KEY." };
      return;
    }

    const stripe = new Stripe(secret);
    const body = req.body || {};

    const applicationId = toSafeString(body.applicationId);
    const sponsorDisplayName = toSafeString(body.sponsorDisplayName);
    const contactName = toSafeString(body.contactName);
    const email = toSafeString(body.email);
    const phone = toSafeString(body.phone);
    const tierId = toSafeString(body.tierId);
    const divisionName = toSafeString(body.divisionName);
    const teamPreference = toSafeString(body.teamPreference);
    const plaqueRequested = !!body.plaqueRequested;
    const teamCount = Number(body.teamCount || 1);

    if (!applicationId || !sponsorDisplayName || !contactName || !email || !phone || !tierId) {
      context.res = { status: 400, body: "Missing required sponsorship fields." };
      return;
    }

    if (tierId === "multi-team" && ![2, 3, 4].includes(teamCount)) {
      context.res = { status: 400, body: "Multi-Team Sponsor requires 2, 3, or 4 teams." };
      return;
    }

    if (tierId === "division-logo" && !divisionName) {
      context.res = { status: 400, body: "Division Logo Sponsor requires divisionName." };
      return;
    }

    const priceBuilder = PRICE_BOOK[tierId];
    if (!priceBuilder) {
      context.res = { status: 400, body: "Unsupported tier." };
      return;
    }

    const line = priceBuilder({ teamCount });
    if (!line) {
      context.res = { status: 400, body: "Invalid quantity for selected tier." };
      return;
    }

    const origin =
      req.headers.origin ||
      `https://${req.headers["x-forwarded-host"] || req.headers.host}`;

    const session = await stripe.checkout.sessions.create({
      mode: "payment",
      customer_email: email,
      line_items: [
        {
          price_data: {
            currency: "usd",
            product_data: { name: `AGSA ${line.label}` },
            unit_amount: line.unitAmount
          },
          quantity: line.quantity
        }
      ],
      success_url: `${origin}/sponsor/thanks?session_id={CHECKOUT_SESSION_ID}&appId=${encodeURIComponent(applicationId)}&tierId=${encodeURIComponent(tierId)}`,
      cancel_url: `${origin}/sponsor/cancel?appId=${encodeURIComponent(applicationId)}&tierId=${encodeURIComponent(tierId)}`,
      metadata: {
        applicationId,
        sponsorDisplayName,
        contactName,
        email,
        phone,
        tierId,
        teamCount: String(teamCount),
        divisionName,
        plaqueRequested: String(plaqueRequested),
        teamPreference
      }
    });

    context.res = {
      status: 200,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ id: session.id, url: session.url })
    };
  } catch (error) {
    context.log.error("Stripe session creation failed", error);
    context.res = { status: 500, body: "Unable to create checkout session." };
  }
};
