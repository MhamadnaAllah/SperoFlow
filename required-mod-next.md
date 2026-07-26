# Required Modifications — Next.js Landing Page Redesign

## Summary of Changes Made to `frontend/`

The monolithic landing page has been refactored into modular components following the **Stitch Landing Page Reference Design** (`stitch_landing_page/`).

### Modified Files
- `frontend/src/app/page.jsx`: Replaced monolithic 855-line file with a clean 30-line section orchestrator.

### New Components Created
- `frontend/src/components/landing/landing.css`: Landing-specific CSS classes for glassmorphism, typography scale, and keyframe animations.
- `frontend/src/components/landing/utils/useScrollReveal.js`: IntersectionObserver hook for scroll animations.
- `frontend/src/components/landing/HeroShaderCanvas.jsx`: WebGL fluid wave canvas background component.
- `frontend/src/components/landing/HeroSection.jsx`: Landing hero banner.
- `frontend/src/components/landing/LandingNavbar.jsx`: Glassmorphic navigation header.
- `frontend/src/components/landing/MethodologySection.jsx`: GTD, Covey, and OKRs 3-column framework grid.
- `frontend/src/components/landing/AgenticCoreSection.jsx`: Intelligent orchestration NLP pipeline widget showcase.
- `frontend/src/components/landing/SanctuarySection.jsx`: Affective Journaling and Vitality Sync cards.
- `frontend/src/components/landing/PricingSection.jsx`: Interactive monthly/annual billing toggle with 3 pricing tiers.
- `frontend/src/components/landing/FaqSection.jsx`: Accordion-based FAQ.
- `frontend/src/components/landing/CtaSection.jsx`: Final glassmorphic CTA card.
- `frontend/src/components/landing/LandingFooter.jsx`: 4-column directory footer.
