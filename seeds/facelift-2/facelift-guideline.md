1. Visual direction

The current website has a functional administrative layout, but it feels generic and partially disconnected from the real Programa Semilla brand. The facelift should preserve the existing structure and workflows, but make the UI feel cleaner, more branded, and more professional.

The new UI should feel:

Institutional but modern.
Clean, light, and easy to scan.
Strongly aligned with the Programa Semilla logo and color palette.
Less “default admin table” and more polished enterprise platform.
2. Brand assets usage
Main logo

Use the official Programa Semilla logo assets provided by the client.

Recommended usage:

Context	Logo
Sidebar expanded	Horizontal logo
Sidebar collapsed	Icon-only logo
Login page / landing screens	Vertical logo
Footer / institutional area	Official multi-logo footer image
Favicons / small brand marks	Icon-only logo

Suggested asset mapping:

/wwwroot/images/brand/programa-semilla-horizontal.png
/wwwroot/images/brand/programa-semilla-vertical.png
/wwwroot/images/brand/programa-semilla-icon-teal.png
/wwwroot/images/brand/programa-semilla-icon-yellow.png
/wwwroot/images/brand/footer-partners.png

The current “Y” placeholder icon in the sidebar should be replaced completely.

3. Color system

Use the client palette as the source of truth.

Primary colors
--color-primary: #008A9E;
--color-primary-light: #42AFA8;

Use #008A9E as the main brand color for:

Primary buttons.
Active navigation state.
Table headers.
Important links.
Focus states.
Main icons.

Use #42AFA8 as a softer supporting teal for:

Badges.
Hover states.
Secondary highlights.
Informational UI elements.
Accent colors
--color-accent-orange: #F9A61C;
--color-accent-yellow: #FFC729;

Use these carefully, not as main UI colors.

Recommended use:

#FFC729: footer top border, subtle highlights, warning accents.
#F9A61C: status indicators, special callouts, “pending” or “attention” states.
Neutral colors

Add a clean neutral system:

--color-bg: #F6F8FA;
--color-surface: #FFFFFF;
--color-border: #DDE5E8;
--color-text: #1F2933;
--color-text-muted: #64748B;
--color-sidebar-bg: #12343B;
--color-sidebar-hover: #174A53;
--color-danger: #D92D20;
--color-success: #168A4A;

Avoid using strong blue for primary actions. The current blue “Crear usuario” button should become teal.

4. Layout guidelines
Overall shell

Keep the current admin layout:

Left sidebar.
Top header.
Main content area.
Footer.

But improve spacing, hierarchy, and branding.

Recommended dimensions:

Sidebar width: 240px
Top bar height: 56px–64px
Main content padding: 32px
Content max width: 1200px–1280px
Border radius: 8px–12px

The current page content feels too centered and narrow. The new version should use the available horizontal space better while keeping a readable max width.

5. Sidebar redesign

The sidebar should become the strongest brand anchor.

Sidebar behavior
Background: dark teal/navy tone.
Logo at the top using the official Programa Semilla horizontal logo.
Menu groups should remain, but with cleaner spacing.
Active item should be clearly highlighted.
Icons should use consistent size and alignment.

Example:

.sidebar {
  background: #12343B;
  color: #D9E6E8;
}

.sidebar .active {
  background: rgba(66, 175, 168, 0.16);
  color: #FFFFFF;
  border-left: 4px solid #42AFA8;
}

.sidebar a:hover {
  background: #174A53;
}
Sidebar logo area

Replace the current circular icon and text with the real logo.

For dark sidebar, prefer the logo version that has enough contrast. If the logo does not contrast well, place it inside a white or very light rounded container.

Example:

[ white rounded logo container ]
Programa Semilla logo
6. Top bar

The current top bar is minimal and works, but it should be polished.

Recommended structure:

Left: current page title or breadcrumb.
Right: current user email and “Cerrar sesión”.
Use subtle border bottom.
Keep background white.

Example:

.topbar {
  height: 60px;
  background: #FFFFFF;
  border-bottom: 1px solid #DDE5E8;
}

The logout link should use the primary teal color instead of the current blue.

7. Page header

Each page should have a consistent header section.

For the Users page:

Usuarios
Administre las cuentas de usuario de la plataforma.
[Crear usuario] [Crear por lote]

Guidelines:

Title: 22–24px, semibold.
Subtitle: 14px, muted.
Actions aligned to the right.
Primary CTA in teal.
Secondary CTA as outlined button.

Example:

.btn-primary {
  background: #008A9E;
  border-color: #008A9E;
  color: #FFFFFF;
}

.btn-primary:hover {
  background: #007789;
}

.btn-secondary {
  background: #FFFFFF;
  border: 1px solid #B9C7CC;
  color: #1F2933;
}
8. Filters area

The current filters are functional but feel flat.

Wrap filters in a light card or structured filter bar.

Recommended:

[ Search input ] [ Role ] [ Status ] [ Fund ] [ Process ]
[ Extra filter dropdown ] [ Apply ] [ Clear filters ]

Guidelines:

Inputs should have consistent height: 38–40px.
Border radius: 8px.
Add clear filter option.
Keep filters above the table.
On smaller screens, filters should wrap into multiple rows.

Example:

.filters-card {
  background: #FFFFFF;
  border: 1px solid #DDE5E8;
  border-radius: 12px;
  padding: 16px;
  margin-bottom: 16px;
}
9. Table redesign

The table is the core of the current page. Keep it, but improve visual quality.

Table header

Use primary teal:

.table thead th {
  background: #008A9E;
  color: #FFFFFF;
  font-weight: 600;
}
Rows

Avoid the current beige alternating rows. It makes the page feel inconsistent with the brand.

Use:

White rows.
Very light teal hover.
Soft row separators.
.table tbody tr {
  background: #FFFFFF;
}

.table tbody tr:hover {
  background: #EFF8F8;
}

.table td {
  border-bottom: 1px solid #E5ECEF;
}
Role badges

Use pill badges with role-specific colors.

Examples:

Solicitante → light blue/teal
Administrador → teal
Revisor → muted teal
Administrador de proveedores → light teal
.badge {
  border-radius: 999px;
  padding: 4px 10px;
  font-size: 12px;
  font-weight: 600;
}
Status badges

Use green only for active status.

.badge-success {
  background: #DFF7E8;
  color: #168A4A;
}
10. Actions column

The current actions column has too many small buttons, which creates visual noise.

Recommended facelift:

Option A, preferred:

[Editar] [⋯]

The kebab/dropdown menu should include:

Reenviar invitación
Restablecer
Inhabilitar

Option B, acceptable:

Keep all buttons, but restyle them with consistent spacing and softer borders.

Danger action:

.btn-danger-outline {
  border: 1px solid #D92D20;
  color: #D92D20;
  background: #FFFFFF;
}

The “Inhabilitar” action should remain visually distinct but not overpower the entire row.

11. Footer redesign

Replace the current footer logo strip with the official footer asset provided by the client.

Footer structure:

[ yellow top border ]
[ official partner/program logos centered ]
© 2026 Programa Semilla · Sistema de Banca para el Desarrollo

Use the provided footer image as the main footer visual.

Recommended CSS:

.footer {
  background: #FFFFFF;
  border-top: 3px solid #FFC729;
  padding: 20px 32px;
  text-align: center;
}

.footer-logos {
  max-width: 1100px;
  width: 100%;
  height: auto;
}

.footer-copy {
  margin-top: 12px;
  font-size: 12px;
  color: #64748B;
}

On mobile, the footer image should scale down and remain readable.

12. Typography

Use a clean system font stack:

font-family: Inter, "Segoe UI", Roboto, Arial, sans-serif;

Recommended scale:

Page title: 22–24px / 600
Section title: 18px / 600
Table header: 13px / 600
Body text: 14px / 400
Small text: 12px / 400
Buttons: 13px / 600
13. UX improvements

Implement the facelift without changing business logic.

Required UX improvements:

Replace placeholder branding with official Programa Semilla logos.
Apply the new color system consistently.
Improve table readability.
Reduce visual noise in the actions column.
Use teal primary buttons instead of blue.
Add consistent spacing and border radius.
Improve footer with official partner logos.
Make filters easier to scan.
Ensure responsive behavior for tablet and mobile.
Maintain accessibility and keyboard navigation.
