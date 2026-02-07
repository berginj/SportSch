import theme from "./src/config/theme.json" assert { type: "json" };

export default {
  content: ["./src/**/*.{astro,html,js,jsx,md,mdx,ts,tsx}"],
  theme: {
    extend: {
      fontFamily: {
        sans: [theme.fonts.body, "system-ui", "sans-serif"],
        display: [theme.fonts.heading, "system-ui", "sans-serif"],
      },
      colors: {
        brand: {
          primary: theme.colors.primary,
          secondary: theme.colors.secondary,
          accent: theme.colors.accent,
          ink: theme.colors.ink,
          mist: theme.colors.mist,
          paper: theme.colors.paper
        }
      },
      boxShadow: {
        card: "0 10px 30px rgba(7, 24, 45, 0.08)"
      }
    },
  },
  plugins: [],
};
