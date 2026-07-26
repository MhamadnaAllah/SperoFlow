# Design System Specification

## 1. Overview & Creative North Star: "The Digital Architect"
This design system rejects the cluttered, "boxy" nature of traditional productivity tools in favor of a **High-End Editorial** layout. Our Creative North Star is **"The Digital Architect."** It envisions the workspace not as a series of constraints, but as a clean, expansive studio where thoughts have room to breathe.

To achieve this, we move beyond the standard "Facebook-style" grid by employing **Intentional Asymmetry**. While the interface remains modular and widget-based, we use varying card widths, overlapping glass layers, and high-contrast typography to create a signature, premium feel. We prioritize cognitive ease through **Tonal Depth** rather than structural lines, ensuring the UI feels like a seamless extension of the user’s focus.

---

## 2. Colors: Depth and Emotional Role-Mapping
The palette is rooted in a deep, authoritative Blue, supported by a sophisticated range of surface neutrals. 

### The "No-Line" Rule
**Explicit Instruction:** Traditional 1px solid borders are strictly prohibited for sectioning. Boundaries must be defined solely through background shifts. For example, a `surface-container-low` widget must sit on a `surface` background. If you feel the need for a line, use a 12px gap of whitespace instead.

### Surface Hierarchy & Nesting
Treat the UI as a physical stack of materials. 
*   **Base Layer:** `surface` (#f7f9fb) – The desk.
*   **Mid Layer:** `surface-container-low` (#f1f4f6) – Large layout containers (e.g., the Sidebar).
*   **Top Layer:** `surface-container-lowest` (#ffffff) – Primary interactive widgets and cards.

### The "Glass & Gradient" Rule
Floating elements (Modals, Popovers, Focus Timer) should utilize **Glassmorphism**. Use `surface-container-lowest` at 80% opacity with a `20px` backdrop-blur. Main CTAs should use a subtle linear gradient: `primary` (#0053dc) to `primary-dim` (#0049c2) at a 135-degree angle to provide "visual soul."

### Role-Based Accents
*   **Work/Professional:** `secondary` (Emerald Green - #006d4a)
*   **Personal/Family:** `tertiary` (Warm Orange - #865400)
*   **Critical/Urgent:** `error` (#ac3434)

---

## 3. Typography: Editorial Authority
We use **Inter** as our sole typeface, relying on extreme weight and scale shifts to establish hierarchy.

| Level | Size | Weight | Tracking | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Display-LG** | 3.5rem | 700 (Bold) | -0.02em | Hero progress stats & Focus Timer digits. |
| **Headline-SM** | 1.5rem | 600 (Semi) | -0.01em | Widget titles and Role headings. |
| **Title-MD** | 1.125rem | 500 (Medium) | 0 | Sub-headers and Card titles. |
| **Body-MD** | 0.875rem | 400 (Regular) | 0.01em | Primary task descriptions and notes. |
| **Label-MD** | 0.75rem | 600 (Semi) | 0.05em | Uppercase tags and metadata. |

---

## 4. Elevation & Depth: The Layering Principle
We convey importance through **Tonal Layering** rather than traditional drop shadows.

*   **Ambient Shadows:** When a widget must "float" (e.g., during a drag interaction), use a shadow with a `32px` blur and `6%` opacity using a tint of `on-surface` (#2d3337). It should look like a soft glow, not a dark smudge.
*   **The "Ghost Border" Fallback:** For accessibility in high-density views, use a `1px` border using `outline-variant` (#acb3b7) at **15% opacity**.
*   **Roundedness Scale:**
    *   **Cards/Widgets:** `xl` (1.5rem) – High-end, soft aesthetic.
    *   **Buttons/Inputs:** `md` (0.75rem) – Professional and functional.
    *   **Progress Pill:** `full` (9999px) – Organic and tactile.

---

## 5. Components: Tactile & Functional

### Modular Widgets (The Core)
*   **Construction:** Use `surface-container-lowest` (#ffffff) with `xl` rounding. 
*   **Separation:** No dividers. Use `spacing-6` (1.5rem) as the standard padding between internal elements.
*   **Interaction:** On hover, a widget should transition from its base state to a `2%` tint of `primary`.

### The Focus Timer (Signature Component)
*   **State:** Use a large Glassmorphic circle. The 'Play' state utilizes a `primary` pulse animation. 
*   **Typography:** Use `display-lg` for the countdown, centered with high breathing room (`spacing-12`).

### Habit Trackers (Dopamine Triggers)
*   **Indicators:** Use `secondary` (#006d4a) for "Success." Use a `0.5s` ease-out expansion animation when a habit is toggled.
*   **Visuals:** Instead of checkboxes, use "micro-progress rings" that fill with a gradient of `secondary` to `secondary-fixed`.

### Navigation & Sidebars
*   **Top Nav:** Persistent `surface-container-lowest` with a `10%` glass blur. Active states use a `4px` bottom "pill" indicator in `primary`.
*   **Role Sidebar:** Uses `surface-container-low` (#f1f4f6). Role icons are housed in `md` (0.75rem) rounded squares with their respective accent colors.

### Input Fields
*   **Design:** Background `surface-container-high`, no border.
*   **Active State:** Transitions to `surface-container-lowest` with a `primary` Ghost Border (20% opacity).

---

## 6. Do’s and Don’ts

### Do
*   **Do** use white space as a structural element. If a screen feels crowded, increase the gap between widgets to `spacing-8` or `spacing-10`.
*   **Do** layer cards. A small "Detail" card can overlap the corner of a "Main" card using the Glassmorphism rule to show relationship.
*   **Do** use `label-sm` for "Work" or "Family" tags, ensuring they are always uppercase with increased letter spacing.

### Don't
*   **Don't** use pure black (#000000) for text. Always use `on-surface` (#2d3337) to maintain a soft, premium feel.
*   **Don't** use standard 1px lines to separate list items. Use a background shift to `surface-container-highest` on hover to define the row.
*   **Don't** use sharp corners. Everything in this system must feel approachable and ergonomic, adhering to the `roundedness` scale.