# Data Model: Programa Semilla Official Brand Alignment (037)

**This feature introduces and alters NO data entities.** It is a presentation-layer visual facelift.

- Database schema is unchanged (FR-032 / SC-014): `git diff main -- src/FundingPlatform.Database/`
  MUST be empty after this feature lands. No tables, columns, indexes, FKs, enums, or seed scripts
  are added or modified.
- No EF entities, DTOs, view models with new data fields, or domain types are created or changed.
  (`PageHeaderViewModel`, `ActionItem`, `StatusPillViewModel`, `CascadingFundFilterModel`, etc. are
  reused as-is.)
- No `AdminAuditEvent` values are added (this is not an admin action surface).

The only "model-like" artifacts are **design tokens** (CSS custom properties) and **brand asset
files**, which are presentation concerns, not data. The authoritative token set and its remap are
specified in `research.md` (D2/D3) and re-stated as a contract in `contracts/ui-and-routes.md`.

## Constitution alignment

- **IV. Schema-First Database Management:** Honored — no dacpac change.
- **I. Clean Architecture:** Honored — changes are confined to the Web presentation layer
  (`wwwroot/css`, `Views/Shared`, `Views/Admin`, `wwwroot/lib/brand`) plus PDF brand image assets.
  No Domain / Application / Infrastructure code is touched.
- **II. Rich Domain Model:** Not engaged — no domain behavior added.
