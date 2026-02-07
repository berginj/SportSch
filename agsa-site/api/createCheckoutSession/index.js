const Stripe = require("stripe");

function asIntAmount(value) {
  const n = Number(value || 0);
  return Math.round(n * 100);
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
    const {
      sponsorName,
      email,
      tierName,
      tierId,
      quantity,
      subtotal,
      processingFee,
      plaqueRequested,
      teamPreference,
      applicationId,
    } = body;

    if (!sponsorName || !email || !tierName || !tierId || !applicationId) {
      context.res = { status: 400, body: "Missing required sponsorship fields." };
      return;
    }

    const qty = Number(quantity || 1);
    const origin = req.headers.origin || `https://${req.headers["x-forwarded-host"] || req.headers.host}`;
    const lineItems = [
      {
        price_data: {
          currency: "usd",
          product_data: { name: `AGSA ${tierName}` },
          unit_amount: asIntAmount(subtotal / qty),
        },
        quantity: qty,
      },
    ];

    if (Number(processingFee || 0) > 0) {
      lineItems.push({
        price_data: {
          currency: "usd",
          product_data: { name: "Processing Fee" },
          unit_amount: asIntAmount(processingFee),
        },
        quantity: 1,
      });
    }

    const session = await stripe.checkout.sessions.create({
      mode: "payment",
      customer_email: email,
      line_items: lineItems,
      success_url: `${origin}/sponsor/thanks?session_id={CHECKOUT_SESSION_ID}&appId=${encodeURIComponent(applicationId)}&tierId=${encodeURIComponent(tierId)}`,
      cancel_url: `${origin}/sponsor/cancel?appId=${encodeURIComponent(applicationId)}`,
      metadata: {
        applicationId,
        sponsorName,
        contactEmail: email,
        tierId,
        quantity: String(qty),
        plaqueRequested: String(!!plaqueRequested),
        teamPreference: teamPreference || "",
      },
    });

    context.res = {
      status: 200,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ id: session.id, url: session.url }),
    };
  } catch (error) {
    context.log.error("Stripe session creation failed", error);
    context.res = { status: 500, body: "Unable to create checkout session." };
  }
};
