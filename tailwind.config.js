/** @type {import("tailwindcss").Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,jsx,ts,tsx}"],
  theme: {
    extend: {
      colors: {
        bg: "var(--bg)",
        panel: "var(--panel)",
        card: "var(--card)",
        text: "var(--text)",
        muted: "var(--muted)",
        border: "var(--border)",
        accent: "var(--accent)",
        danger: "var(--danger)",
        warn: "var(--warn)",
        ok: "var(--ok)"
      },
      boxShadow: {
        card: "0 8px 30px rgba(0,0,0,0.25)"
      }
    }
  },
  plugins: []
};
