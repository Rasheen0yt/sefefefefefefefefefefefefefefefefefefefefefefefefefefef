---
name: Lumina Monolith
colors:
  surface: '#131313'
  surface-dim: '#131313'
  surface-bright: '#393939'
  surface-container-lowest: '#0e0e0e'
  surface-container-low: '#1b1b1b'
  surface-container: '#1f1f1f'
  surface-container-high: '#2a2a2a'
  surface-container-highest: '#353535'
  on-surface: '#e2e2e2'
  on-surface-variant: '#cfc4c5'
  inverse-surface: '#e2e2e2'
  inverse-on-surface: '#303030'
  outline: '#988e90'
  outline-variant: '#4c4546'
  surface-tint: '#c6c6c6'
  primary: '#c6c6c6'
  on-primary: '#303030'
  primary-container: '#000000'
  on-primary-container: '#757575'
  inverse-primary: '#5e5e5e'
  secondary: '#c6c6c7'
  on-secondary: '#2f3131'
  secondary-container: '#454747'
  on-secondary-container: '#b4b5b5'
  tertiary: '#c6c6c6'
  on-tertiary: '#303030'
  tertiary-container: '#000000'
  on-tertiary-container: '#757575'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#e2e2e2'
  primary-fixed-dim: '#c6c6c6'
  on-primary-fixed: '#1b1b1b'
  on-primary-fixed-variant: '#474747'
  secondary-fixed: '#e2e2e2'
  secondary-fixed-dim: '#c6c6c7'
  on-secondary-fixed: '#1a1c1c'
  on-secondary-fixed-variant: '#454747'
  tertiary-fixed: '#e2e2e2'
  tertiary-fixed-dim: '#c6c6c6'
  on-tertiary-fixed: '#1b1b1b'
  on-tertiary-fixed-variant: '#474747'
  background: '#131313'
  on-background: '#e2e2e2'
  surface-variant: '#353535'
typography:
  headline-xl:
    fontFamily: Hanken Grotesk
    fontSize: 64px
    fontWeight: '700'
    lineHeight: '1.1'
    letterSpacing: -0.04em
  headline-lg:
    fontFamily: Hanken Grotesk
    fontSize: 40px
    fontWeight: '600'
    lineHeight: '1.2'
    letterSpacing: -0.02em
  headline-lg-mobile:
    fontFamily: Hanken Grotesk
    fontSize: 32px
    fontWeight: '600'
    lineHeight: '1.2'
  body-md:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.6'
    letterSpacing: 0em
  label-sm:
    fontFamily: Geist
    fontSize: 12px
    fontWeight: '500'
    lineHeight: '1.0'
    letterSpacing: 0.1em
  code-md:
    fontFamily: Geist
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  unit: 8px
  container-max: 1200px
  gutter: 24px
  margin-mobile: 20px
  margin-desktop: 64px
  stack-lg: 4rem
  stack-md: 2rem
  stack-sm: 1rem
---

## Brand & Style

This design system is built on the philosophy of **Monochromatic Hyper-Minimalism** punctuated by **Synthetic Vitality**. It targets high-end productivity, creative tools, or AI-native interfaces where focus is paramount. 

The aesthetic is characterized by:
- **Absolute Contrast:** Leveraging the stark relationship between pure black (#000000) and pure white (#FFFFFF) to establish immediate hierarchy.
- **The "AI RGB" Pulse:** A singular, vibrant exception to the monochromatic rule. This multi-colored gradient glow represents intelligence, activity, and state changes, mimicking a digital "nervous system."
- **Invisible Architecture:** Heavy use of whitespace and precise alignment to create a sense of premium quality and calm.
- **Modernity:** A clean, futuristic feel that is both authoritative and innovative.

## Colors

The palette is strictly restricted to eliminate visual noise.
- **Base:** Pure Black (#000000) serves as the primary canvas for the dark mode experience.
- **Contrast:** Pure White (#FFFFFF) is used for typography, icons, and primary action surfaces.
- **The Lumina Gradient:** A vibrant spectrum (Blue, Purple, Red, Gold) reserved strictly for:
    - Active AI processing states.
    - Focus rings around active input fields.
    - Thin "breathing" borders on primary calls to action.
    - Subtle under-glow for active navigation tabs.

No grays are permitted. Depth is achieved through opacity shifts of white (e.g., secondary text at 60% white) rather than introducing hex-coded grays.

## Typography

The system utilizes **Hanken Grotesk** for its sharp, contemporary geometry and exceptional legibility in high-contrast environments. **Geist** is used for labels and technical data to provide a precise, developer-friendly touch.

- **Headlines:** Set with tight letter-spacing and bold weights to command attention against the black void.
- **Body:** Generous line-height (1.6) ensures long-form readability. 
- **Hierarchy:** Established through scale and opacity. Primary content is 100% white; secondary content is 60% white; tertiary hints are 40% white.

## Layout & Spacing

This design system employs a **Fluid Grid** with oversized margins to evoke an editorial, premium feel. 

- **The Void:** Use "Stack" spacing (64px+) between major sections to allow the monochromatic elements to "breathe."
- **Alignment:** Strictly adhered to a 12-column grid on desktop. Elements should feel "anchored" to the grid lines.
- **Responsive Behavior:** On mobile, margins reduce but whitespace remains the primary separator—avoid using dividers/lines wherever possible, opting for spatial grouping instead.

## Elevation & Depth

In a pure black environment, traditional shadows are invisible. Depth is created through:
- **Tonal Layering:** "Elevated" surfaces are not gray; they are defined by a 1px white border with 10% opacity, or simply by the presence of the AI RGB glow.
- **The Glow Bloom:** When an element is active or "intelligent," it emits a `20px` to `40px` Gaussian blur glow using the RGB gradient. This glow should feel like it is sitting *behind* the black surface, bleeding out from the edges.
- **Stark Overlays:** Modals and menus utilize a 100% white background with black text to "pop" aggressively from the black canvas, creating an immediate focal shift.

## Shapes

The shape language is **Soft (0.25rem)**. This slight rounding takes the "edge" off the brutalist black/white contrast, making the interface feel engineered yet approachable. 

- **Interactive Elements:** Buttons and inputs use the standard 0.25rem radius.
- **Large Containers:** Cards or sections can use `rounded-lg` (0.5rem) to differentiate from smaller components.
- **The RGB Border:** When the "breathing" AI effect is applied to a shape, the gradient stroke must follow the corner radius perfectly.

## Components

### Buttons
- **Primary:** Solid white background, black text. No shadow. On hover, apply a thin 1px RGB gradient border.
- **Secondary:** Transparent background, 1px white border (20% opacity), white text. 
- **AI Action:** Pure black background, white text, with a continuous "breathing" RGB glow (0% to 60% opacity animation) as an outer shadow.

### Input Fields
- **Default:** Minimalist bottom-border only (white, 20% opacity).
- **Focus:** The bottom border transforms into a 2px RGB gradient line, with a subtle 4px vertical glow bleed.

### List Items
- Separated by space, not lines. 
- **Active State:** A vertical RGB gradient bar (2px wide) appears to the far left of the item.

### AI Status Indicators
- A small (8px) circular dot that pulses with the RGB gradient. When "thinking," the dot expands into a 16px blurred orb.

### Cards
- Pure black background with a 1px white border at very low (10%) opacity. Text inside follows the standard white hierarchy.