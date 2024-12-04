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
				primary: { DEFAULT: "#0B2545" },
				secondary: { DEFAULT: "#134074" },
				info: { DEFAULT: "#8DA9C4" },
				text: { DEFAULT: "eef4ed" },
		},
	},
	plugins: [],
}
}
