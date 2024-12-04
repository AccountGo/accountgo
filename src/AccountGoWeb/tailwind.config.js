/** @type {import('tailwindcss').Config} */
module.exports = {
	content: [
		"./Pages/**/*.cshtml",
		"./Views/**/*.cshtml",
		"./wwwroot/**/*.html",
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
	plugins: [],
}
}
