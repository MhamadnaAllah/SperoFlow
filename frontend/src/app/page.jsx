"use client";

import { useRef } from "react";
import "../components/landing/landing.css";
import useScrollReveal from "../components/landing/utils/useScrollReveal";
import LandingNavbar from "../components/landing/LandingNavbar";
import HeroSection from "../components/landing/HeroSection";
import MethodologySection from "../components/landing/MethodologySection";
import AgenticCoreSection from "../components/landing/AgenticCoreSection";
import SanctuarySection from "../components/landing/SanctuarySection";
import PricingSection from "../components/landing/PricingSection";
import FaqSection from "../components/landing/FaqSection";
import CtaSection from "../components/landing/CtaSection";
import LandingFooter from "../components/landing/LandingFooter";

export default function LandingPage() {
  const rootRef = useRef(null);
  useScrollReveal(rootRef);

  return (
    <div
      ref={rootRef}
      className="landing-root bg-surface text-on-surface font-body antialiased overflow-x-hidden min-h-screen"
    >
      <LandingNavbar />
      <main>
        <HeroSection />
        <MethodologySection />
        <AgenticCoreSection />
        <SanctuarySection />
        <PricingSection />
        <FaqSection />
        <CtaSection />
      </main>
      <LandingFooter />
    </div>
  );
}
