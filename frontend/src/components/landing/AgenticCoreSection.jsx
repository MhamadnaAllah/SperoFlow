import React from 'react';

export default function AgenticCoreSection() {
  return (
    <section className="py-32 px-6 lg:px-12 bg-background" id="modules">
      <div className="max-w-7xl mx-auto">
        <div className="grid lg:grid-cols-2 gap-20 items-center">
          
          {/* Left Column — AI Orchestrator Widget */}
          <div className="relative" data-reveal>
            <div className="absolute inset-0 bg-primary-container/20 rounded-full blur-3xl mix-blend-multiply"></div>
            <div className="bg-surface-container-lowest rounded-xl p-6 ambient-shadow border-none relative z-10 widget-card">
              
              <div className="flex justify-between items-center mb-8">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full bg-primary-container flex items-center justify-center">
                    <span className="material-symbols-outlined text-primary">smart_toy</span>
                  </div>
                  <h3 className="text-title-md text-on-surface font-medium">AI Orchestrator Active</h3>
                </div>
                <div className="flex items-center gap-2 px-3 py-1 bg-surface-container rounded-full">
                  <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse"></span>
                  <span className="text-label-md text-on-surface-variant">Processing</span>
                </div>
              </div>

              <div className="space-y-6">
                <div className="bg-surface rounded-lg p-4 border border-outline-variant/30">
                  <p className="text-label-md text-on-surface-variant mb-2">Raw Brain Dump Input</p>
                  <p className="text-body-md text-on-surface italic">"I'm super stressed about the marketing designs, I need to review them by Friday."</p>
                </div>

                <div className="flex justify-center">
                  <span className="material-symbols-outlined text-outline-variant">arrow_downward</span>
                </div>

                <div className="pl-8 border-l-2 border-primary-container space-y-4">
                  <div className="flex items-start gap-3">
                    <span className="material-symbols-outlined text-primary text-xl mt-0.5">psychology_alt</span>
                    <div>
                      <p className="text-label-md font-medium text-on-surface">Intent Extraction: Actionable Task</p>
                    </div>
                  </div>
                  <div className="flex items-start gap-3">
                    <span className="material-symbols-outlined text-error text-xl mt-0.5">mood_bad</span>
                    <div>
                      <p className="text-label-md font-medium text-on-surface">Sentiment Analysis: Elevated Stress Detected</p>
                    </div>
                  </div>
                  <div className="flex items-start gap-3">
                    <span className="material-symbols-outlined text-tertiary text-xl mt-0.5">event</span>
                    <div>
                      <p className="text-label-md font-medium text-on-surface">Temporal Parsing: Due Friday</p>
                    </div>
                  </div>
                </div>

                <div className="flex justify-center">
                  <span className="material-symbols-outlined text-outline-variant">arrow_downward</span>
                </div>

                <div className="bg-white rounded-lg p-4 shadow-sm border border-outline-variant/20">
                  <p className="text-label-md text-primary mb-3">Structured Task Pipeline Output</p>
                  <div className="flex items-start gap-3">
                    <span className="material-symbols-outlined text-outline mt-0.5">check_box_outline_blank</span>
                    <div>
                      <p className="text-title-md text-on-surface mb-2 font-medium">Review Q3 Marketing Designs</p>
                      <div className="flex gap-2">
                        <span className="px-2 py-1 bg-error-container/20 text-error text-label-md rounded">P1: Urgent</span>
                        <span className="px-2 py-1 bg-surface-container-highest text-on-surface-variant text-label-md rounded">Tag: Deep Work</span>
                      </div>
                    </div>
                  </div>
                </div>

              </div>
            </div>
          </div>

          {/* Right Column — Narrative */}
          <div className="space-y-8" data-reveal>
            <div>
              <h2 className="text-display-lg text-on-background mb-4">The Agentic Core.</h2>
              <p className="text-headline-sm text-on-surface-variant font-normal">More than organization. It's intelligent orchestration.</p>
            </div>
            
            <p className="text-body-lg text-on-surface-variant">
              Traditional task managers wait for you to organize them. SperoFlow's Agentic Core acts as your executive assistant—extracting intent, understanding sentiment, and structuring your unstructured thoughts into a prioritized pipeline automatically.
            </p>

            <ul className="space-y-6">
              <li className="flex gap-4">
                <div className="w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
                  <span className="material-symbols-outlined text-primary">account_tree</span>
                </div>
                <div>
                  <h4 className="text-title-md text-on-surface font-medium mb-1">Semantic Parsing</h4>
                  <p className="text-body-md text-on-surface-variant">Transforms natural language brain dumps into structured, actionable items with context and tags.</p>
                </div>
              </li>
              <li className="flex gap-4">
                <div className="w-12 h-12 rounded-full bg-secondary/10 flex items-center justify-center shrink-0">
                  <span className="material-symbols-outlined text-secondary">balance</span>
                </div>
                <div>
                  <h4 className="text-title-md text-on-surface font-medium mb-1">Algorithmic Prioritization</h4>
                  <p className="text-body-md text-on-surface-variant">Continuously re-evaluates your pipeline based on deadlines, energy levels, and psychological load.</p>
                </div>
              </li>
            </ul>
          </div>

        </div>
      </div>
    </section>
  );
}
