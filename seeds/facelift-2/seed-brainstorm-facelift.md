You are working on the Programa Semilla web platform UI. The goal is to perform a visual facelift only, without changing backend logic, business rules, routes, permissions, or data behavior.

Use the official Programa Semilla branding assets provided by the client. Replace the current placeholder sidebar logo/icon with the real Programa Semilla logo. Use the horizontal logo for the expanded sidebar, the icon-only logo for compact/small contexts, and the vertical logo for login or large brand areas if applicable.

Use the following brand color system:

Primary:
- #008A9E as the main brand teal.
- #42AFA8 as the secondary/supporting teal.

Accents:
- #F9A61C as orange accent.
- #FFC729 as yellow accent.

Neutrals:
- Background: #F6F8FA.
- Surface/card: #FFFFFF.
- Border: #DDE5E8.
- Main text: #1F2933.
- Muted text: #64748B.
- Sidebar background: #12343B.
- Sidebar hover: #174A53.
- Success: #168A4A.
- Danger: #D92D20.

Update the main admin layout as follows:

1. Sidebar
- Use a dark teal/navy sidebar.
- Place the official Programa Semilla logo at the top.
- Use consistent icon alignment and spacing.
- Highlight the active menu item with a teal accent and a left border.
- Keep the existing menu structure and labels.

2. Top bar
- Keep a clean white top bar.
- Add a subtle bottom border.
- Keep the current user email and logout action on the right.
- Replace blue logout/link styling with the brand teal.

3. Page header
- Standardize all pages with a clear title, subtitle, and right-aligned actions.
- On the Users page, keep:
  - Title: “Usuarios”
  - Subtitle: “Administre las cuentas de usuario de la plataforma.”
  - Primary action: “Crear usuario”
  - Secondary action: “Crear por lote”
- Primary buttons must use #008A9E, not blue.

4. Filters
- Group filters inside a clean white card or structured filter area.
- Use consistent input heights, border radius, and spacing.
- Keep all current filter functionality.
- Add or preserve an Apply button.
- If easy, add a Clear filters action.

5. Tables
- Keep the existing data and columns.
- Use #008A9E for table headers with white text.
- Use white table rows with subtle separators.
- Add a soft hover state using a very light teal background.
- Avoid beige alternating rows.
- Improve padding and alignment.
- Keep role and status badges as rounded pills.

6. Actions column
- Reduce visual noise.
- Prefer showing “Editar” as the main visible action and moving secondary actions into a dropdown menu.
- If that is too invasive, keep all current buttons but restyle them consistently.
- Danger actions such as “Inhabilitar” must use a red outline style.

7. Footer
- Replace the current footer logo strip with the official footer image provided by the client.
- Add a yellow top border using #FFC729.
- Center the footer logos.
- Keep the copyright text:
  “© 2026 Programa Semilla · Sistema de Banca para el Desarrollo”

8. Typography
- Use Inter, Segoe UI, Roboto, Arial, sans-serif.
- Use clean font weights and consistent sizing.
- Page titles should be around 22–24px and semibold.
- Body/table text should be around 14px.

9. Responsiveness
- Ensure the layout remains usable on desktop, tablet, and mobile.
- Filters should wrap naturally.
- Tables should support horizontal scrolling on small screens.
- Footer logos should scale responsively.

10. Accessibility
- Preserve keyboard navigation.
- Add visible focus states using the primary teal.
- Ensure sufficient contrast for text, buttons, and navigation.
- Do not rely on color alone for status meaning.

Important constraints:
- Do not change backend logic.
- Do not change database models.
- Do not change permissions.
- Do not remove existing functionality.
- Do not rename existing routes or actions.
- This is a UI/UX facelift using the official Programa Semilla brand identity.
