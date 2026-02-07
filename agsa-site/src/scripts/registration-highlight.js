const today = new Date();
today.setHours(0, 0, 0, 0);

document.querySelectorAll("[data-highlight-until]").forEach((card) => {
  const until = card.getAttribute("data-highlight-until");
  if (!until) return;

  const cutoff = new Date(`${until}T23:59:59`);
  const badge = card.querySelector("[data-highlight-badge]");
  if (!badge) return;

  const isActive = !Number.isNaN(cutoff.valueOf()) && today.valueOf() <= cutoff.valueOf();
  if (isActive) {
    badge.removeAttribute("hidden");
    card.classList.add("ring-2", "ring-brand-accent", "ring-offset-2");
  } else {
    badge.setAttribute("hidden", "");
    card.classList.remove("ring-2", "ring-brand-accent", "ring-offset-2");
  }
});
