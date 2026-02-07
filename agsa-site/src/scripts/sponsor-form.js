const tiers = JSON.parse(document.getElementById("sponsor-tiers-data")?.textContent || "[]");
const settings = JSON.parse(document.getElementById("sponsor-settings-data")?.textContent || "{}");

const form = document.getElementById("sponsor-form");
if (!form) {
  // Page safety.
} else {
  const stepOrder = ["tier", "info", "review", "pay"];
  const stepLabel = form.querySelector("[data-step-label]");
  const nextBtn = form.querySelector("[data-next]");
  const backBtn = form.querySelector("[data-back]");
  const payBtn = form.querySelector("[data-pay]");
  const submitCheckBtn = form.querySelector("[data-submit-check]");
  const tierInput = form.querySelector("[data-tier-select-input]");
  const teamCountWrapper = form.querySelector("[data-team-count-wrapper]");
  const divisionWrapper = form.querySelector("[data-division-wrapper]");
  const logoWrapper = form.querySelector("[data-logo-wrapper]");
  const reviewContent = form.querySelector("[data-review-content]");
  const summaryEl = document.querySelector("[data-summary]");
  const mobileSummary = document.querySelector("[data-mobile-summary]");
  const mobileScrollBtn = document.querySelector("[data-mobile-scroll]");

  let currentStep = "tier";
  let applicationId = "";

  function money(value) {
    return `$${Number(value || 0).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  function ensureAppId() {
    if (!applicationId) applicationId = window.crypto?.randomUUID?.() || `app-${Date.now()}`;
    return applicationId;
  }

  function getTier() {
    return tiers.find((tier) => tier.id === form.elements.tierId.value) || null;
  }

  function getTeamCount(tier) {
    if (!tier?.isTeamQuantityAllowed) return 1;
    const raw = Number(form.elements.teamCount?.value || 1);
    const value = Number.isFinite(raw) ? Math.max(1, Math.floor(raw)) : 1;
    if (Array.isArray(tier.allowedQuantities) && tier.allowedQuantities.length > 0) {
      return tier.allowedQuantities.includes(value) ? value : tier.allowedQuantities[0];
    }
    return value;
  }

  function computePrice(tier, teamCount) {
    if (!tier) return 0;
    if (tier.pricingMode === "per_team") return Number(tier.basePrice || 0) * teamCount;
    if (tier.pricingMode === "fixed_by_quantity") {
      const map = tier.multiTeamPricing || {};
      return Number(map[String(teamCount)] || 0);
    }
    return Number(tier.basePrice || 0);
  }

  function isTier4(tier) {
    return tier?.id === "division-logo";
  }

  function getData() {
    const tier = getTier();
    const teamCount = getTeamCount(tier);
    const total = computePrice(tier, teamCount);
    return {
      applicationId: ensureAppId(),
      sponsorDisplayName: (form.elements.sponsorDisplayName?.value || "").trim(),
      contactName: (form.elements.contactName?.value || "").trim(),
      email: (form.elements.email?.value || "").trim(),
      phone: (form.elements.phone?.value || "").trim(),
      address: (form.elements.address?.value || "").trim(),
      tierId: tier?.id || "",
      tierName: tier?.name || "",
      termMonths: Number(settings.sponsorshipTermMonths || 12),
      teamCount,
      divisionName: (form.elements.divisionName?.value || "").trim(),
      teamPreference: (form.elements.teamPreference?.value || "").trim(),
      plaqueRequested: !!form.elements.plaqueRequested?.checked,
      notes: (form.elements.notes?.value || "").trim(),
      total,
      benefits: tier?.benefits || [],
      jerseyLogoAllowed: !!tier?.jerseyLogoAllowed,
      barcroftBannerIncluded: !!tier?.barcroftBannerIncluded
    };
  }

  function updateTierSpecificFields() {
    const tier = getTier();
    const showTeamCount = !!tier?.isTeamQuantityAllowed;
    const allowLogo = isTier4(tier);

    teamCountWrapper.hidden = !showTeamCount;
    divisionWrapper.hidden = !allowLogo;
    logoWrapper.hidden = !allowLogo;

    if (!showTeamCount) form.elements.teamCount.value = "1";
    if (!allowLogo) {
      form.elements.divisionName.value = "";
      form.elements.logoFile.value = "";
    }

    const allowed = tier?.allowedQuantities || [];
    if (showTeamCount && allowed.length > 0) {
      const current = Number(form.elements.teamCount.value || allowed[0]);
      if (!allowed.includes(current)) {
        form.elements.teamCount.value = String(allowed[0]);
      }
    }
  }

  function renderSummary() {
    const data = getData();
    if (!data.tierId) {
      summaryEl.innerHTML = "<p>Select a tier to begin.</p>";
      if (mobileSummary) mobileSummary.textContent = "Choose a tier to see total.";
      return;
    }

    const teamLine = data.tierId === "community-team" || data.tierId === "multi-team"
      ? `<p class="m-0">Teams sponsored: <strong>${data.teamCount}</strong></p>`
      : "";
    const divisionLine = data.tierId === "division-logo" && data.divisionName
      ? `<p class="m-0">Division: <strong>${data.divisionName}</strong></p>`
      : "";

    summaryEl.innerHTML = `
      <p class="m-0 text-xs uppercase tracking-wide text-brand-primary">${data.tierName}</p>
      <p class="mt-1 text-sm">Term: <strong>${data.termMonths} months (1 full year)</strong></p>
      ${teamLine}
      ${divisionLine}
      <p class="mt-2">Barcroft Field 3 banner: <strong>${data.barcroftBannerIncluded ? "Included" : "Not included"}</strong></p>
      <p class="mt-2">Jersey logo: <strong>${data.jerseyLogoAllowed ? "Allowed (Tier 4 only)" : "Name-only placement"}</strong></p>
      <p class="mt-3 text-base font-bold text-brand-secondary">Total: ${money(data.total)}</p>
      <ul class="mt-2 list-disc pl-5 text-xs text-slate-600">
        ${data.benefits.slice(0, 4).map((b) => `<li>${b}</li>`).join("")}
      </ul>
    `;

    if (mobileSummary) mobileSummary.textContent = `${data.tierName}: ${money(data.total)}`;
  }

  function isHoneypotTriggered() {
    return (form.elements._gotcha?.value || "").trim().length > 0;
  }

  function validateTierStep() {
    const tier = getTier();
    if (!tier) return "Choose a sponsorship tier.";
    const teamCount = getTeamCount(tier);
    if (tier.id === "multi-team" && ![2, 3, 4].includes(teamCount)) {
      return "Multi-Team Sponsor requires 2, 3, or 4 teams.";
    }
    if (tier.id === "division-logo" && !(form.elements.divisionName.value || "").trim()) {
      return "Division Logo Sponsor requires a division selection.";
    }
    return "";
  }

  function validateInfoStep() {
    const required = ["sponsorDisplayName", "contactName", "email", "phone", "address"];
    for (const fieldName of required) {
      const value = (form.elements[fieldName]?.value || "").toString().trim();
      if (!value) return "Please complete all required sponsor contact fields.";
    }
    if (isHoneypotTriggered()) return "Submission blocked.";
    return "";
  }

  function renderReview() {
    const data = getData();
    const divisionLine = data.divisionName ? `<p><strong>Division:</strong> ${data.divisionName}</p>` : "";
    const teamLine = data.tierId === "community-team" || data.tierId === "multi-team"
      ? `<p><strong>Teams sponsored:</strong> ${data.teamCount}</p>`
      : "";
    const logoLine = data.jerseyLogoAllowed
      ? "<p><strong>Jersey logo:</strong> Allowed (Tier 4 exclusive)</p>"
      : "<p><strong>Jersey logo:</strong> Not included (name-only)</p>";

    reviewContent.innerHTML = `
      <p><strong>Sponsor display name:</strong> ${data.sponsorDisplayName}</p>
      <p><strong>Contact:</strong> ${data.contactName} (${data.email})</p>
      <p><strong>Tier:</strong> ${data.tierName}</p>
      <p><strong>Term:</strong> ${data.termMonths} months (1 full year)</p>
      ${teamLine}
      ${divisionLine}
      ${logoLine}
      <p><strong>Total:</strong> ${money(data.total)}</p>
      <p><strong>Barcroft Field 3 banner:</strong> ${data.barcroftBannerIncluded ? "Included" : "Not included"}</p>
      <p><strong>Plaque requested:</strong> ${data.plaqueRequested ? "Yes" : "No"}</p>
      ${data.teamPreference ? `<p><strong>Team preferences:</strong> ${data.teamPreference}</p>` : ""}
      ${data.notes ? `<p><strong>Notes:</strong> ${data.notes}</p>` : ""}
      <p class="mt-3"><strong>Benefits:</strong></p>
      <ul class="list-disc pl-5">${data.benefits.map((b) => `<li>${b}</li>`).join("")}</ul>
    `;
  }

  function setStep(step) {
    currentStep = step;
    stepOrder.forEach((stepName) => {
      const el = form.querySelector(`[data-step="${stepName}"]`);
      if (el) el.classList.toggle("hidden", stepName !== step);
    });

    const idx = stepOrder.indexOf(step);
    stepLabel.textContent = `Step ${idx + 1} of ${stepOrder.length}`;
    backBtn.hidden = idx === 0;
    nextBtn.hidden = idx >= stepOrder.length - 1;
    payBtn.hidden = step !== "pay";
    submitCheckBtn.hidden = step !== "pay";
    nextBtn.textContent = step === "review" ? "Continue to payment" : "Continue";
  }

  async function submitSponsorship(data, paymentMode) {
    const response = await fetch("/api/submitSponsorship", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ...data, paymentMode })
    });
    if (!response.ok) {
      throw new Error("Unable to submit sponsorship application.");
    }
    const payload = await response.json();
    if (payload?.applicationId) applicationId = payload.applicationId;
    return payload;
  }

  async function startStripeCheckout(data) {
    const response = await fetch("/api/createCheckoutSession", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });
    if (!response.ok) {
      const msg = await response.text();
      throw new Error(msg || "Unable to create checkout session.");
    }
    const payload = await response.json();
    if (!payload?.url) throw new Error("Missing checkout URL.");
    window.location.href = payload.url;
  }

  function nextStep() {
    const idx = stepOrder.indexOf(currentStep);
    if (idx < stepOrder.length - 1) setStep(stepOrder[idx + 1]);
  }

  function prevStep() {
    const idx = stepOrder.indexOf(currentStep);
    if (idx > 0) setStep(stepOrder[idx - 1]);
  }

  tierInput.addEventListener("change", () => {
    updateTierSpecificFields();
    renderSummary();
  });
  form.elements.teamCount?.addEventListener("change", renderSummary);
  form.elements.divisionName?.addEventListener("change", renderSummary);

  form.querySelectorAll("[data-tier-select]").forEach((btn) => {
    btn.addEventListener("click", () => {
      tierInput.value = btn.getAttribute("data-tier-select");
      tierInput.dispatchEvent(new Event("change", { bubbles: true }));
      form.scrollIntoView({ behavior: "smooth", block: "start" });
    });
  });

  nextBtn.addEventListener("click", () => {
    if (currentStep === "tier") {
      const msg = validateTierStep();
      if (msg) return alert(msg);
      return nextStep();
    }
    if (currentStep === "info") {
      const msg = validateInfoStep();
      if (msg) return alert(msg);
      return nextStep();
    }
    if (currentStep === "review") {
      return nextStep();
    }
  });

  backBtn.addEventListener("click", () => prevStep());

  payBtn.addEventListener("click", async () => {
    try {
      ensureAppId();
      const data = getData();
      const saved = await submitSponsorship(data, "stripe_card");
      await startStripeCheckout({ ...data, applicationId: saved.applicationId || data.applicationId });
    } catch (error) {
      alert(error.message || "Unable to continue to payment.");
    }
  });

  submitCheckBtn.addEventListener("click", async () => {
    try {
      ensureAppId();
      const data = getData();
      const saved = await submitSponsorship(data, "check");
      const appId = saved.applicationId || data.applicationId;
      localStorage.setItem(`agsa-sponsor-check-${appId}`, JSON.stringify({ ...data, applicationId: appId }));
      window.location.href = `/sponsor/check?appId=${encodeURIComponent(appId)}`;
    } catch {
      alert("Unable to submit at this time. Please email sponsors@agsafastpitch.com.");
    }
  });

  mobileScrollBtn?.addEventListener("click", () => {
    form.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  form.addEventListener("input", () => {
    if (currentStep === "review") renderReview();
  });

  setStep("tier");
  updateTierSpecificFields();
  renderSummary();
}
