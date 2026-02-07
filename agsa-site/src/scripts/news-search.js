const searchInput = document.querySelector("[data-news-search]");
const cards = Array.from(document.querySelectorAll("[data-news-card]"));
const status = document.querySelector("[data-news-status]");

if (searchInput && status) {
  const applyFilter = () => {
    const query = searchInput.value.trim().toLowerCase();
    let visible = 0;
    cards.forEach((card) => {
      const text = (card.textContent || "").toLowerCase();
      const show = !query || text.includes(query);
      card.classList.toggle("hidden", !show);
      if (show) visible += 1;
    });
    status.textContent = `${visible} article${visible === 1 ? "" : "s"} shown`;
  };

  searchInput.addEventListener("input", applyFilter);
  applyFilter();
}
