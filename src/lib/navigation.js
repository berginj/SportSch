const CALENDAR_FILTER_KEYS = [
  "division",
  "dateFrom",
  "dateTo",
  "showSlots",
  "showEvents",
  "status",
  "slotType",
  "teamId",
];

const OFFERS_FILTER_KEYS = ["division", "slotType"];

function buildPathname(url) {
  const search = url.searchParams.toString();
  return `${url.pathname}${search ? `?${search}` : ""}`;
}

function writeSearchParams(url, setParams = {}, clearParams = []) {
  clearParams.forEach((key) => url.searchParams.delete(key));

  Object.entries(setParams).forEach(([key, value]) => {
    if (value == null || value === "") {
      url.searchParams.delete(key);
      return;
    }
    url.searchParams.set(key, String(value));
  });
}

function navigateWithHash(setTab, tab, setParams = {}, clearParams = []) {
  if (typeof window === "undefined") return;

  const base = window.location.origin && window.location.origin !== "null"
    ? window.location.origin
    : "http://localhost";
  const url = new URL(window.location.href || "/", base);
  writeSearchParams(url, setParams, clearParams);
  const nextPath = buildPathname(url);
  const nextHash = tab ? `#${tab}` : "";
  const previousHash = window.location.hash;
  window.history.replaceState({}, "", `${nextPath}${nextHash}`);
  if (typeof setTab === "function") {
    setTab(tab);
    return;
  }
  if (previousHash !== nextHash) {
    window.dispatchEvent(new Event("hashchange"));
  }
}

export function navigateToManageTab(setTab, manageTab = "") {
  navigateWithHash(setTab, "manage", { manageTab }, ["adminSection"]);
}

export function navigateToAdminSection(setTab, adminSection = "") {
  navigateWithHash(setTab, "admin", { adminSection }, ["manageTab"]);
}

export function navigateToOffersTab(setTab, { division = "", slotType = "" } = {}) {
  navigateWithHash(
    setTab,
    "offers",
    { division, slotType },
    ["manageTab", "adminSection", ...CALENDAR_FILTER_KEYS.filter((key) => !OFFERS_FILTER_KEYS.includes(key))]
  );
}

export function navigateToCalendarTab(
  setTab,
  {
    division = "",
    dateFrom = "",
    dateTo = "",
    showSlots = true,
    showEvents = true,
    statuses = [],
    slotType = "",
    teamId = "",
  } = {}
) {
  navigateWithHash(
    setTab,
    "calendar",
    {
      division,
      dateFrom,
      dateTo,
      showSlots: showSlots ? "1" : "",
      showEvents: showEvents ? "1" : "",
      status: Array.isArray(statuses) && statuses.length ? statuses.join(",") : "",
      slotType,
      teamId,
    },
    ["manageTab", "adminSection", ...CALENDAR_FILTER_KEYS]
  );
}
