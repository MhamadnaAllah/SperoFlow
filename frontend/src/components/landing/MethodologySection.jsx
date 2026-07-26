export default function MethodologySection() {
  return (
    <section className="py-24 bg-surface-container-low px-6 lg:px-12 relative overflow-hidden" id="methodology" data-reveal>
      <div className="max-w-7xl mx-auto">
        <div className="text-center max-w-3xl mx-auto mb-20">
          <span className="text-label-md text-secondary tracking-widest uppercase block mb-4">
            The Foundation
          </span>
          <h2 className="text-display-lg text-on-surface mb-6">
            Methodological Convergence.
          </h2>
          <p className="text-title-md text-on-surface-variant">
            We don't invent new productivity fads. We build digital architecture around proven paradigms: Getting Things Done (GTD), the Covey Time Matrix, and Objectives & Key Results (OKRs).
          </p>
        </div>

        <div className="grid lg:grid-cols-3 gap-8">
          {/* Card 1 */}
          <div className="bg-surface-container-lowest rounded-xl p-8 ambient-shadow ghost-border widget-card flex flex-col border-none" data-reveal data-reveal-delay="0">
            <div className="w-12 h-12 rounded-lg bg-surface flex items-center justify-center mb-6">
              <span className="material-symbols-outlined text-primary">inbox</span>
            </div>
            <h3 className="text-title-md text-on-surface mb-3 font-semibold">Capture & Clarify</h3>
            <p className="text-body-md text-on-surface-variant mb-6">
              Our "Brain Dump" module acts as your ubiquitous inbox. Utilizing NLP, it automatically clarifies raw text into actionable items, honoring the core tenet of GTD: getting it out of your head.
            </p>
            <div className="mt-auto pt-6 border-t border-surface-container-highest">
              <span className="text-label-md text-on-surface-variant">Integrated Framework: GTD®</span>
            </div>
          </div>

          {/* Card 2 */}
          <div className="bg-surface-container-lowest rounded-xl p-8 ambient-shadow ghost-border widget-card flex flex-col border-none transform lg:-translate-y-4" data-reveal data-reveal-delay="100">
            <div className="w-12 h-12 rounded-lg bg-surface flex items-center justify-center mb-6">
              <span className="material-symbols-outlined text-secondary">grid_view</span>
            </div>
            <h3 className="text-title-md text-on-surface mb-3 font-semibold">Strategic Quadrants</h3>
            <p className="text-body-md text-on-surface-variant mb-6">
              Tasks don't just sit in a list. They are mapped onto a digital Covey Matrix. The interface visually prioritizes Quadrant II (Important, Not Urgent) to foster proactive growth over reactive firefighting.
            </p>
            <div className="mt-auto pt-6 border-t border-surface-container-highest">
              <span className="text-label-md text-on-surface-variant">Integrated Framework: EISENHOWER/COVEY</span>
            </div>
          </div>

          {/* Card 3 */}
          <div className="bg-surface-container-lowest rounded-xl p-8 ambient-shadow ghost-border widget-card flex flex-col border-none" data-reveal data-reveal-delay="200">
            <div className="w-12 h-12 rounded-lg bg-surface flex items-center justify-center mb-6">
              <span className="material-symbols-outlined text-tertiary">flag</span>
            </div>
            <h3 className="text-title-md text-on-surface mb-3 font-semibold">Vertical Alignment</h3>
            <p className="text-body-md text-on-surface-variant mb-6">
              Daily tasks are tethered directly to Key Results, which flow up to overarching Objectives. This creates a clear line of sight from today's small action to tomorrow's massive outcome.
            </p>
            <div className="mt-auto pt-6 border-t border-surface-container-highest">
              <span className="text-label-md text-on-surface-variant">Integrated Framework: OKRs</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
