/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Pages/**/*.{cshtml,razor}",
    "./Views/**/*.{cshtml,razor}",
    "./Components/**/*.{cshtml,razor}",
    "./wwwroot/**/*.{html,js}",
  ],
  theme: {
    extend: {
      colors: {
        darkPrimary: { DEFAULT: "#0B2545" },
        darkSecondary: { DEFAULT: "#134074" },
        lightPrimary: { DEFAULT: "#8DA9C4" },
        lightSecondary: { DEFAULT: "#eef4ed" },
      },
    },
  },
  plugins: [],
};
