---
description: Angular coding conventions for the ui/ frontend — component style, state, DI, and styling
alwaysApply: false
globs:
  - "ui/**/*.ts"
  - "ui/**/*.html"
  - "ui/**/*.scss"
---

## Component style

- All components are **standalone** (`standalone: true`) — never use NgModule declarations.
- Use `inject()` for dependency injection, not constructor parameters.
- Style files are **SCSS** (`.scss`), one per component.

## State management

- Prefer Angular **signals** (`signal()`, `computed()`, `effect()`) over plain class properties.
- Use `toSignal()` to convert observables to signals at the boundary.
- Keep RxJS for async operations (HTTP, debounce), but expose results via signals where possible.

## UI library

- **Angular Material** is the component library — use it for all UI primitives (buttons, inputs, cards, dialogs, icons, menus, etc.).
- Import only the specific Material modules needed in each component's `imports: []` array.

## Styling

- Use **CSS custom properties** (defined in `src/styles.scss`) for all colors and design tokens — never hardcode hex values.
- Key variables: `--primaryColor`, `--secondaryColor`, `--main-bg-color`, `--main-foreground-color`, `--ms-action-background`, `--ms-danger-background`, `--mainFontFamily`, `--paragraphFontFamily`.
- Component-specific styles go in the component's `.scss` file; global/shared styles go in `src/styles.scss`.

## File placement

- New reusable components → `ui/src/app/shared/components/<name>/`
- New page components → `ui/src/app/pages/<name>/`
- New services → `ui/src/app/services/`
- New TypeScript models/interfaces → `ui/src/app/models/`
