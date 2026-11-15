/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        navy: { DEFAULT: "#0f2a3d", 700: "#16374f", 600: "#1d4560" },
        teal: { DEFAULT: "#0e7c86", 600: "#0b6870", 50: "#e6f4f5" },
        accent: "#1fb59a",
        page: "#f4f6f8",
        line: "#e3e8ec",
      },
      borderRadius: { card: "12px" },
      fontFamily: { sans: ["Inter", "system-ui", "Segoe UI", "Roboto", "sans-serif"] },
    },
  },
  plugins: [],
};
