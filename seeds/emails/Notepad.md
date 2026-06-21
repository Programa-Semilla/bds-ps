Acting as a senior product designer, UX writer, and email template engineer.

I will provide:

1. A text file with the current email contents used by the ALIA system.
2. A reference email design that shows the layout style I like.
3. Brand assets for Programa Semilla, including logos, footer logos, and color palette.

Your task is to create detailed implementation requirements for a UI lift of all ALIA transactional emails.

ALIA is a professional digital system for managing non-reimbursable seed-capital funds from Banco de Desarrollo / Programa Semilla.

## Goal

Redesign all current system emails so they:

- Preserve the original meaning and intent of every email.
- Keep all dynamic variables exactly as provided, such as `{nombre}` and `{enlace_plataforma}`.
- Use a professional, trustworthy, modern visual style inspired by the reference email layout.
- Apply Programa Semilla’s brand identity, logos, footer, and color palette.
- Improve readability, hierarchy, and call-to-action clarity.
- Are suitable for government, development banking, entrepreneurship, and grant-management communications.

## Design Direction

Use the reference email as the structural inspiration:

- Centered email container.
- Header area with logo and optional branded hero section.
- Clear greeting.
- Concise body copy.
- Highlighted main action or next step.
- CTA button when a valid link or link placeholder exists.
- Helpful secondary information section when relevant.
- Footer with Programa Semilla branding, contact information, legal note, and partner/footer logos where appropriate.

Use this color palette:

- Primary teal: `#008a9e`
- Secondary teal: `#42afa8`
- Orange accent: `#f9a61c`
- Yellow accent: `#ffc729`
- White / light neutral backgrounds for readability

## Link Handling Rules

Some emails may include explicit link placeholders, such as `{enlace_plataforma}`. When a link placeholder exists:

- Convert it into a clear CTA button.
- Keep the placeholder unchanged in the href.
- Also include a fallback plain-text link if needed for email compatibility.

When the email text implies a link but no placeholder is provided:

- Do not invent a URL.
- Do not create a fake placeholder.
- Mention in the requirements that the coding agent should omit the CTA button unless a valid link variable is available.

## Content Rules

For each email:

- Keep the subject line.
- Preserve the original message intent.
- Improve wording only if needed for clarity, professionalism, or consistency.
- Maintain the Spanish language and Costa Rican voseo style where already used.
- Do not remove important warnings, status updates, or support instructions.
- Keep automatic-message notes.
- Standardize formatting, spacing, punctuation, and sign-off.

## Output Required

Create a complete implementation specification with:

1. **Global email design system**
    - Layout structure
    - Typography recommendations
    - Color usage
    - Button styles
    - Icon/image usage
    - Header and footer rules
    - Responsive/mobile behavior
    - Accessibility requirements
    - Email-client compatibility notes
2. **Reusable email template structure**
    - Header
    - Hero or title block
    - Body content
    - CTA area
    - Status/info card
    - Footer
3. **Per-email requirements**  
    For every email found in the text file, provide:
    - Email name
    - Subject
    - Purpose
    - Recommended layout
    - Main CTA, if applicable
    - Required variables
    - Notes for missing links
    - Content hierarchy
    - Any suggested copy refinements
4. **Asset usage guidance**
    - Which logo to use in the header
    - How to use the footer logo strip
    - When to use icons or supporting imagery
    - Suggestions for finding royalty-free or official images online that match the theme of entrepreneurship, funding, small businesses, financial inclusion, and business growth
5. **Developer-ready requirements**
    - HTML email constraints
    - Inline CSS requirements
    - Max-width recommendations
    - Button fallback behavior
    - Image alt text
    - Dark mode considerations
    - Testing checklist
6. **Acceptance criteria**
    - All emails are accounted for.
    - All original variables are preserved.
    - No fake URLs are created.
    - Brand colors and assets are used consistently.
    - Emails render correctly on desktop and mobile.
    - Tone is professional, warm, and institutional.

Use the provided reference email and brand files as visual inspiration, but adapt the design to ALIA and Programa Semilla rather than copying the Beefree branding.