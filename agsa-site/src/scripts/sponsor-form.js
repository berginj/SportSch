const tiers = JSON.parse(document.getElementById("sponsor-tiers-data")?.textContent || "[]");
const settings = JSON.parse(document.getElementById("sponsor-settings-data")?.textContent || "{}");

const form = document.getElementById("sponsor-form");
if (!form) {
  // page safety
} else {
  const stepLabel = form.querySelector("[data-step-label]");
  const detailsStep = form.querySelector('[data-step="details"]');
  const reviewStep = form.querySelector('[data-step="review"]');
  const reviewContent = form.querySelector("[data-review-content]");
  const nextBtn = form.querySelector("[data-next]");
  const backBtn = form.querySelector("[data-back]");
  const payBtn = form.querySelector("[data-pay]");
  const submitCheckBtn = form.querySelector("[data-submit-check]");
  const tierInput = form.querySelector("[data-tier-select-input]");
  const quantityWrapper = form.querySelector("[data-quantity-wrapper]");
  const summaryEl = document.querySelector("[data-summary]");
  const mobileSummary = document.querySelector("[data-mobile-summary]");
  const mobileScrollBtn = document.querySelector("[data-mobile-scroll]");

  let currentStep = "details";
  let applicationId = "";

  function getTierById(id) {
    return tiers.find((t) => t.id === id) || null;
  }

  function getQuantity(tier) {
    const raw = Number(form.elements.quantity?.value || 1);
    if (!tier?.isTeamQuantityAllowed) return 1;
    return Number.isFinite(raw) && raw > 0 ? Math.floor(raw) : 1;
  }

  function computeFee(subtotal) {
    if (settings.processingFeeMode === "flat") return Number(settings.flatFeeAmount || 0);
    if (settings.processingFeeMode === "percent") return Math.round(subtotal * Number(settings.percentFeeRate || 0) * 100) / 100;
    return 0;
  }

  function collectData() {
    const tier = getTierById(form.elements.tierId.value);
    const quantity = getQuantity(tier);
    const subtotal = tier ? tier.price * quantity : 0;
    const processingFee = computeFee(subtotal);
    const total = subtotal + processingFee;
    return {
      sponsorName: (form.elements.sponsorName.value || "").trim(),
      contactName: (form.elements.contactName.value || "").trim(),
      email: (form.elements.email.value || "").trim(),
      phone: (form.elements.phone.value || "").trim(),
      address: (form.elements.address.value || "").trim(),
      tierId: tier?.id || "",
      tierName: tier?.name || "",
      quantity,
      teamPreference: (form.elements.teamPreference.value || "").trim(),
      plaqueRequested: !!form.elements.plaqueRequested.checked,
      notes: (form.elements.notes.value || "").trim(),
      subtotal,
      processingFee,
      total,
      benefits: tier?.benefits || [],
      applicationId,
    };
  }

  function updateQuantityVisibility() {
    const tier = getTierById(form.elements.tierId.value);
    const show = !!tier?.isTeamQuantityAllowed;
    quantityWrapper.hidden = !show;
    if (!show) form.elements.quantity.value = "1";
  }

  function renderSummary() {
    const d = collectData();
    if (!d.tierId) {
      summaryEl.innerHTML = "<p>Select a tier to begin.</p>";
      if (mobileSummary) mobileSummary.textContent = "Choose a tier to see total.";
      return;
    }
    const feeLabel = d.processingFee > 0 ? `<p class="m-0">Processing fee: <strong>$${d.processingFee.toFixed(2)}</strong></p>` : "";
    summaryEl.innerHTML = `
      <p class="m-0 text-xs uppercase tracking-wide text-brand-primary">${d.tierName}</p>
      <p class="mt-1 text-sm">Quantity: <strong>${d.quantity}</strong></p>
      <p class="m-0">Subtotal: <strong>$${d.subtotal.toFixed(2)}</strong></p>
      ${feeLabel}
      <p class="mt-2 text-base font-bold text-brand-secondary">Total: $${d.total.toFixed(2)}</p>
      <ul class="mt-2 list-disc pl-5 text-xs text-slate-600">
        ${d.benefits.map((b) => `<li>${b}</li>`).join("")}
      </ul>
    `;
    if (mobileSummary) mobileSummary.textContent = `${d.tierName}: $${d.total.toFixed(2)}`;
  }

  function validateDetails() {
    const requiredNames = ["sponsorName", "contactName", "email", "phone", "address", "tierId"];
    for (const name of requiredNames) {
      const val = (form.elements[name]?.value || "").toString().trim();
      if (!val) return false;
    }
    return true;
  }

  function renderReview() {
    const d = collectData();
    reviewContent.innerHTML = `
      <p><strong>Sponsor:</strong> ${d.sponsorName}</p>
      <p><strong>Contact:</strong> ${d.contactName} (${d.email})</p>
      <p><strong>Tier:</strong> ${d.tierName}</p>
      <p><strong>Quantity:</strong> ${d.quantity}</p>
      <p><strong>Subtotal:</strong> $${d.subtotal.toFixed(2)}</p>
      <p><strong>Processing fee:</strong> $${d.processingFee.toFixed(2)}</p>
      <p><strong>Total:</strong> $${d.total.toFixed(2)}</p>
      <p><strong>Benefits:</strong></p>
      <ul class="list-disc pl-5">${d.benefits.map((b) => `<li>${b}</li>`).join("")}</ul>
      <p class="mt-2"><strong>Plaque requested:</strong> ${d.plaqueRequested ? "Yes" : "No"}</p>
      ${d.teamPreference ? `<p><strong>Team preference:</strong> ${d.teamPreference}</p>` : ""}
      ${d.notes ? `<p><strong>Notes:</strong> ${d.notes}</p>` : ""}
    `;
  }

  function setStep(step) {
    currentStep = step;
    const inDetails = step === "details";
    detailsStep.classList.toggle("hidden", !inDetails);
    reviewStep.classList.toggle("hidden", inDetails);
    stepLabel.textContent = inDetails ? "Step 1 of 2" : "Step 2 of 2";
    backBtn.hidden = inDetails;
    nextBtn.hidden = !inDetails;
    payBtn.hidden = inDetails;
    submitCheckBtn.hidden = inDetails;
  }

  async function postSubmission(data, paymentMethod) {
    if (!settings.submissionEndpoint || settings.submissionEndpoint.includes("PLACEHOLDER")) return;
    await fetch(settings.submissionEndpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ...data, paymentMethod }),
    });
  }

  async function startStripeCheckout(data) {
    const response = await fetch("/.auth/functions/createCheckoutSession", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    });
    if (!response.ok) {
      const msg = await response.text();
      throw new Error(msg || "Unable to create checkout session.");
    }
    const json = await response.json();
    if (!json?.url) throw new Error("Missing checkout URL.");
    window.location.href = json.url;
  }

  function ensureAppId() {
    if (!applicationId) {
      applicationId = (window.crypto?.randomUUID?.() || `app-${Date.now()}`);
    }
  }

  tierInput.addEventListener("change", () => {
    updateQuantityVisibility();
    renderSummary();
  });
  form.elements.quantity?.addEventListener("input", renderSummary);
  form.querySelectorAll("[data-tier-select]").forEach((btn) => {
    btn.addEventListener("click", () => {
      tierInput.value = btn.getAttribute("data-tier-select");
      tierInput.dispatchEvent(new Event("change", { bubbles: true }));
      form.scrollIntoView({ behavior: "smooth", block: "start" });
    });
  });

  nextBtn.addEventListener("click", () => {
    if (!validateDetails()) {
      alert("Please complete all required fields before review.");
      return;
    }
    ensureAppId();
    renderReview();
    setStep("review");
  });

  backBtn.addEventListener("click", () => setStep("details"));

  payBtn.addEventListener("click", async () => {
    try {
      ensureAppId();
      const data = collectData();
      await postSubmission(data, "card");
      await startStripeCheckout(data);
    } catch (err) {
      alert(err.message || "Unable to continue to payment.");
    }
  });

  submitCheckBtn.addEventListener("click", async () => {
    try {
      ensureAppId();
      const data = collectData();
      await postSubmission(data, "check");
      window.location.href = `/sponsor/thanks?appId=${encodeURIComponent(applicationId)}&tierId=${encodeURIComponent(data.tierId)}&offline=1`;
    } catch {
      alert("Unable to submit at this time. Please email sponsors@agsafastpitch.com.");
    }
  });

  mobileScrollBtn?.addEventListener("click", () => {
    form.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  updateQuantityVisibility();
  renderSummary();
  setStep("details");
}
