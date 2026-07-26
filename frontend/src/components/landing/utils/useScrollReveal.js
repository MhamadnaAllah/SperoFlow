"use client";

import { useEffect, useRef } from "react";

/**
 * Scroll-reveal hook: observes elements with [data-reveal] and adds
 * the `revealed` class when they enter the viewport. Supports staggered
 * delays via data-reveal-delay attribute.
 */
export default function useScrollReveal(containerRef) {
  const observerRef = useRef(null);

  useEffect(() => {
    const container = containerRef?.current ?? document;
    const elements = container.querySelectorAll("[data-reveal]");
    if (elements.length === 0) return;

    observerRef.current = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            const delay = entry.target.getAttribute("data-reveal-delay");
            if (delay) {
              entry.target.style.transitionDelay = `${delay}ms`;
            }
            entry.target.classList.add("revealed");
            observerRef.current?.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.15, rootMargin: "0px 0px -60px 0px" }
    );

    elements.forEach((el) => observerRef.current.observe(el));

    return () => {
      observerRef.current?.disconnect();
    };
  }, [containerRef]);
}
