import React from 'react';

export default function SanctuarySection() {
  return (
    <section className="py-24 bg-surface px-6 lg:px-12 relative" data-reveal>
      <div className="max-w-7xl mx-auto">
        
        <div className="mb-16">
          <h2 className="text-display-lg text-on-background mb-4">The Multi-Dimensional Sanctuary.</h2>
          <p className="text-title-md text-on-surface-variant max-w-2xl">
            Productivity without vitality is burnout waiting to happen. SperoFlow integrates quantitative energy tracking with qualitative emotional reflection.
          </p>
        </div>

        <div className="grid lg:grid-cols-2 gap-8">
          
          {/* Card A — Affective Journaling */}
          <div className="bg-surface-container-lowest rounded-xl p-8 ambient-shadow border-none widget-card">
            <div className="flex items-center gap-4 mb-6">
              <div className="w-12 h-12 rounded-xl bg-tertiary/10 flex items-center justify-center">
                <span className="material-symbols-outlined text-tertiary">menu_book</span>
              </div>
              <div>
                <p className="text-label-md text-on-surface-variant tracking-wider uppercase">Sentiment & Reflection</p>
                <h3 className="text-title-lg text-on-surface font-medium">Affective Journaling</h3>
              </div>
            </div>
            
            <p className="text-body-md text-on-surface-variant mb-8">
              A private space for free-form reflection. SperoFlow subtly analyzes entries to track emotional well-being over time, helping you identify what drains or sustains you.
            </p>

            <div className="h-48 bg-surface-container-low rounded-lg p-4 flex items-end gap-2 relative mt-auto">
              <p className="absolute top-4 left-4 text-label-sm text-on-surface-variant">Weekly Sentiment Trend</p>
              
              <div className="flex-1 bg-tertiary/40 rounded-t-sm relative group transition-all hover:bg-tertiary/50" style={{ height: '40%' }}>
                <div className="opacity-0 group-hover:opacity-100 absolute -top-8 left-1/2 -translate-x-1/2 bg-surface text-on-surface text-label-sm px-2 py-1 rounded shadow-sm whitespace-nowrap transition-opacity pointer-events-none">Calm</div>
              </div>
              <div className="flex-1 bg-error/40 rounded-t-sm relative group transition-all hover:bg-error/50" style={{ height: '70%' }}>
                <div className="opacity-0 group-hover:opacity-100 absolute -top-8 left-1/2 -translate-x-1/2 bg-surface text-on-surface text-label-sm px-2 py-1 rounded shadow-sm whitespace-nowrap transition-opacity pointer-events-none">Stressed</div>
              </div>
              <div className="flex-1 bg-tertiary/60 rounded-t-sm relative group transition-all hover:bg-tertiary/70" style={{ height: '50%' }}>
                <div className="opacity-0 group-hover:opacity-100 absolute -top-8 left-1/2 -translate-x-1/2 bg-surface text-on-surface text-label-sm px-2 py-1 rounded shadow-sm whitespace-nowrap transition-opacity pointer-events-none">Neutral</div>
              </div>
              <div className="flex-1 bg-secondary/60 rounded-t-sm relative group transition-all hover:bg-secondary/70" style={{ height: '85%' }}>
                <div className="opacity-0 group-hover:opacity-100 absolute -top-8 left-1/2 -translate-x-1/2 bg-surface text-on-surface text-label-sm px-2 py-1 rounded shadow-sm whitespace-nowrap transition-opacity pointer-events-none">Inspired</div>
              </div>
              <div className="flex-1 bg-secondary/80 rounded-t-sm relative group transition-all hover:bg-secondary/90" style={{ height: '95%' }}>
                <div className="opacity-0 group-hover:opacity-100 absolute -top-8 left-1/2 -translate-x-1/2 bg-surface text-on-surface text-label-sm px-2 py-1 rounded shadow-sm whitespace-nowrap transition-opacity pointer-events-none">Flow State</div>
              </div>
            </div>
          </div>

          {/* Card B — Vitality Sanctuary */}
          <div className="bg-surface-container-lowest rounded-xl p-8 ambient-shadow border-none widget-card flex flex-col">
            <div className="flex items-center gap-4 mb-6">
              <div className="w-12 h-12 rounded-xl bg-secondary/10 flex items-center justify-center">
                <span className="material-symbols-outlined text-secondary">vital_signs</span>
              </div>
              <div>
                <p className="text-label-md text-on-surface-variant tracking-wider uppercase">Biological Rhythm Sync</p>
                <h3 className="text-title-lg text-on-surface font-medium">Vitality Sanctuary</h3>
              </div>
            </div>

            <p className="text-body-md text-on-surface-variant mb-8">
              Log your energy states. The system adapts, suggesting intense cognitive work during your peaks and restful, administrative tasks during troughs.
            </p>

            <div className="mb-6 mt-auto">
              <div className="flex justify-between items-center mb-2">
                <span className="text-label-md text-on-surface font-medium">Current Energy Level</span>
                <span className="text-label-md text-secondary font-medium">High (Peak)</span>
              </div>
              <div className="h-2 bg-surface-container-high rounded-full overflow-hidden">
                <div className="h-full bg-secondary w-[85%] rounded-full"></div>
              </div>
            </div>

            <div className="bg-surface rounded-lg p-4 flex gap-4 items-start border border-outline-variant/30">
              <span className="material-symbols-outlined text-secondary mt-0.5">bolt</span>
              <div>
                <p className="text-title-sm text-on-surface mb-1 font-medium">AI Recommendation</p>
                <p className="text-body-sm text-on-surface-variant">Prime time for deep work. Avoid checking email for the next 90 minutes.</p>
              </div>
            </div>
          </div>

        </div>
      </div>
    </section>
  );
}
