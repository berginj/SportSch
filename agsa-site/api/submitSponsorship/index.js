const crypto = require("crypto");
const sgMail = require("@sendgrid/mail");

function safe(input) {
  return String(input || "").trim();
}

function buildApplicationId() {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  return `app-${Date.now()}`;
}

module.exports = async function (context, req) {
  try {
    const body = req.body || {};

    if (safe(body._gotcha)) {
      context.res = { status: 200, jsonBody: { applicationId: buildApplicationId(), accepted: true } };
      return;
    }

    const payload = {
      applicationId: safe(body.applicationId) || buildApplicationId(),
      paymentMode: safe(body.paymentMode) || "stripe_card",
      sponsorDisplayName: safe(body.sponsorDisplayName),
      contactName: safe(body.contactName),
      email: safe(body.email),
      phone: safe(body.phone),
      address: safe(body.address),
      tierId: safe(body.tierId),
      tierName: safe(body.tierName),
      teamCount: Number(body.teamCount || 1),
      divisionName: safe(body.divisionName),
      teamPreference: safe(body.teamPreference),
      plaqueRequested: !!body.plaqueRequested,
      notes: safe(body.notes),
      total: Number(body.total || 0)
    };

    if (!payload.sponsorDisplayName || !payload.contactName || !payload.email || !payload.phone || !payload.address || !payload.tierId) {
      context.res = { status: 400, body: "Missing required sponsorship fields." };
      return;
    }

    const recipients = (process.env.SPONSOR_NOTIFICATION_EMAILS || "sponsors@agsafastpitch.com,marketing@agsafastpitch.com")
      .split(",")
      .map((value) => value.trim())
      .filter(Boolean);

    const sender = process.env.SPONSOR_EMAIL_FROM;
    const sendgridKey = process.env.SENDGRID_API_KEY;

    if (sendgridKey && sender && recipients.length > 0) {
      sgMail.setApiKey(sendgridKey);
      const lines = [
        `Application ID: ${payload.applicationId}`,
        `Payment mode: ${payload.paymentMode}`,
        `Sponsor display name: ${payload.sponsorDisplayName}`,
        `Contact name: ${payload.contactName}`,
        `Email: ${payload.email}`,
        `Phone: ${payload.phone}`,
        `Address: ${payload.address}`,
        `Tier: ${payload.tierName} (${payload.tierId})`,
        `Team count: ${payload.teamCount}`,
        `Division: ${payload.divisionName || "n/a"}`,
        `Team preference: ${payload.teamPreference || "n/a"}`,
        `Plaque requested: ${payload.plaqueRequested ? "yes" : "no"}`,
        `Total: $${payload.total.toFixed(2)}`,
        `Notes: ${payload.notes || "n/a"}`
      ];

      await sgMail.send({
        to: recipients,
        from: sender,
        replyTo: payload.email,
        subject: `AGSA Sponsorship Application ${payload.applicationId}`,
        text: lines.join("\n")
      });
    } else {
      context.log.warn("Sponsor submission email not sent. Configure SENDGRID_API_KEY, SPONSOR_EMAIL_FROM, and SPONSOR_NOTIFICATION_EMAILS.");
      context.log.info("Sponsorship payload", payload);
    }

    context.res = {
      status: 200,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        applicationId: payload.applicationId,
        accepted: true
      })
    };
  } catch (error) {
    context.log.error("submitSponsorship failed", error);
    context.res = { status: 500, body: "Unable to submit sponsorship application." };
  }
};
