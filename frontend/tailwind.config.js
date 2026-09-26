/** @type {import('tailwindcss').Config} */

/*
 * Tokens vivem como variaveis CSS em index.css (canais RGB), para o
 * modificador de opacidade do Tailwind continuar valendo: bg-surface-2/60.
 * Mesmo desenho do Pickia. Cor se muda no :root, nunca solta no componente.
 */
const token = (name) => `rgb(var(--${name}) / <alpha-value>)`

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        surface: { 0: token('surface-0'), 1: token('surface-1'), 2: token('surface-2'), 3: token('surface-3') },
        ink: { 1: token('ink-1'), 2: token('ink-2'), 3: token('ink-3'), 4: token('ink-4') },
        line: { DEFAULT: token('line'), strong: token('line-strong') },
        accent: { DEFAULT: token('accent'), hover: token('accent-hover'), ink: token('accent-ink') },
        warn: token('warn'),
        danger: token('danger'),
        info: token('info'),
      },
      fontFamily: {
        sans: ['Inter', 'Segoe UI', 'system-ui', 'sans-serif'],
      },
      maxWidth: { page: '1120px' },
    },
  },
  plugins: [],
}
