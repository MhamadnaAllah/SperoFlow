"use client";

import { useState } from "react";

const SAMPLE_SUGGESTIONS = [
  "Become a senior Fullstack Developer building high-scale real-time SaaS dashboards with Next.js and Firebase",
  "Transition into a Machine Learning Engineer specializing in LLMs, Prompt Engineering, and RAG architectures",
  "Master Cloud Architecture, infrastructure as code, Kubernetes deployments, and modern serverless APIs",
];

export default function VisionaryGoalScreen({ roadmap, onGenerate, onViewRoadmap, loading }) {
  const [goalText, setGoalText] = useState(roadmap.goalText || "");
  const [horizon, setHorizon] = useState(roadmap.horizon || "1yr");
  const [loadingStep, setLoadingStep] = useState(0);

  const loadingMessages = [
    "Traversing knowledge graph for prerequisite nodes...",
    "Running topological sort on dependency chains...",
    "Synthesizing structured learning timeline with LLM...",
    "Enriching modules with RAG document citations...",
    "Assembling final curriculum structure...",
  ];

  const handleGenerateClick = async () => {
    if (!goalText.trim()) return;

    let step = 0;
    const interval = setInterval(() => {
      if (step < loadingMessages.length - 1) {
        step += 1;
        setLoadingStep(step);
      }
    }, 850);

    try {
      await onGenerate(goalText, horizon);
    } finally {
      clearInterval(interval);
      setLoadingStep(0);
    }
  };

  return (
    <div className="space-y-12 animate-fade-in">
      {/* Header section */}
      <div className="text-center max-w-2xl mx-auto space-y-3">
        <h1 className="text-4xl md:text-5xl font-headline font-extrabold text-on-surface tracking-tight leading-tight">
          Create Your Learning Horizon
        </h1>
        <p className="text-sm text-on-surface-variant leading-relaxed">
          Input your career aspiration or learning path to compile a deep-dive syllabus, interactive roadmap checkpoints, and documentation sources.
        </p>
      </div>

      {/* Main card panel */}
      <div className="bg-surface-container-lowest border border-outline-variant rounded-xl shadow-sm max-w-4xl mx-auto overflow-hidden">
        <div className="p-6 md:p-8 bg-gradient-to-br from-primary/5 to-transparent border-b border-outline-variant/40">
          <div className="flex items-center gap-1.5 text-[10px] font-mono font-bold text-primary uppercase tracking-widest mb-3">
            <span className="material-symbols-outlined text-sm" style={{ fontSize: '14px' }}>psychology</span>
            Manifesting Potential
          </div>
          <h2 className="text-2xl md:text-3xl font-headline font-bold text-on-surface mb-6">
            What is your visionary goal?
          </h2>

          {/* Goal textarea input */}
          <div className="space-y-2">
            <label className="text-[10px] font-headline font-bold text-on-surface-variant uppercase tracking-wider block">
              Context &amp; Nuance
            </label>
            <textarea
              id="visionary-goal-input"
              rows={4}
              value={goalText}
              onChange={(e) => setGoalText(e.target.value)}
              placeholder="Describe the impact or specific outcome you desire..."
              className="w-full bg-surface-container-lowest border border-outline-variant rounded-lg p-4 font-headline text-sm text-on-surface focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors placeholder:text-outline/70"
            />
          </div>

          {/* Suggestions chips */}
          <div className="mt-4">
            <span className="text-[10px] font-headline font-medium text-outline block mb-2">Try a suggestion:</span>
            <div className="flex flex-wrap gap-2">
              {SAMPLE_SUGGESTIONS.map((suggestion, index) => (
                <button
                  key={index}
                  onClick={() => setGoalText(suggestion)}
                  className="bg-surface-container-low hover:bg-surface-container-high border border-outline-variant/40 px-3 py-1 text-[11px] rounded-full text-on-surface-variant transition-colors text-left"
                >
                  {suggestion.slice(0, 50)}...
                </button>
              ))}
            </div>
          </div>

          {/* Configuration selections */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-6 mt-8 pt-6 border-t border-outline-variant/40">
            {/* Horizon Picker */}
            <div className="flex items-center gap-4">
              <span className="text-[10px] font-headline font-bold text-on-surface-variant uppercase tracking-wider">
                Horizon:
              </span>
              <div className="flex bg-surface-container-low p-1 rounded-lg border border-outline-variant/40">
                {["3mo", "1yr", "5yr"].map((h) => (
                  <button
                    key={h}
                    onClick={() => setHorizon(h)}
                    className={`px-4 py-1.5 text-[11px] font-headline font-bold rounded-md transition-all ${
                      horizon === h
                        ? "bg-primary text-on-primary shadow-sm"
                        : "text-on-surface-variant hover:text-primary"
                    }`}
                  >
                    {h}
                  </button>
                ))}
              </div>
            </div>

            {/* Generate Action Button */}
            <button
              id="generate-ai-path-button"
              disabled={loading || !goalText.trim()}
              onClick={handleGenerateClick}
              className={`px-6 py-2.5 rounded-lg text-sm font-headline font-bold transition-all flex items-center justify-center gap-2 select-none ${
                goalText.trim()
                  ? "bg-primary text-on-primary hover:bg-primary/95 cursor-pointer shadow-md hover:-translate-y-0.5 active:translate-y-0"
                  : "bg-outline/20 text-outline cursor-not-allowed"
              }`}
            >
              {loading ? (
                <>
                  <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                  </svg>
                  <span>Compiling Pathway...</span>
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>menu</span>
                  Generate AI Path
                </>
              )}
            </button>
          </div>
        </div>

        {/* Dynamic Generating Steps */}
        {loading && (
          <div className="p-6 bg-surface-container-low border-b border-outline-variant/30 flex items-center gap-4 transition-all animate-pulse">
            <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center text-primary shrink-0 animate-spin">
              <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>hourglass_empty</span>
            </div>
            <div>
              <div className="text-[10px] font-headline font-bold text-primary uppercase tracking-wider">AI Operations Lifecycle</div>
              <div className="text-sm text-on-surface-variant mt-0.5 font-medium">
                {loadingMessages[loadingStep]}
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Algorithmic Pathway Output Card */}
      {!loading && (
        <div className="max-w-4xl mx-auto space-y-4 animate-fade-in-up">
          <div className="flex items-center justify-between border-b border-outline-variant pb-2">
            <h3 className="text-lg font-headline font-bold text-on-surface tracking-tight">
              Algorithmic Path Output
            </h3>
            <button
              onClick={onViewRoadmap}
              className="text-primary hover:text-primary-container text-[11px] font-headline font-bold flex items-center gap-1 hover:underline select-none"
            >
              Accept &amp; Populate Matrix &rarr;
            </button>
          </div>

          <div className="bg-surface-container-lowest border border-outline-variant rounded-xl p-6 flex flex-col md:flex-row md:items-center justify-between gap-6 shadow-sm">
            <div className="space-y-2 max-w-2xl">
              <span className="bg-secondary-container text-on-secondary-container text-[10px] font-mono font-bold px-2 py-0.5 rounded uppercase tracking-wider border border-secondary/20">
                Career Path
              </span>
              <h4 id="career-path-output-title" className="text-xl md:text-2xl font-headline font-extrabold text-on-surface">
                {roadmap.title}
              </h4>
              <p className="text-sm text-on-surface-variant leading-relaxed">
                {roadmap.description}
              </p>
            </div>

            <button
              id="view-roadmap-button"
              onClick={onViewRoadmap}
              className="bg-primary hover:bg-primary-container text-on-primary font-headline font-bold text-sm px-5 py-3 rounded-lg shadow-sm hover:shadow-md transition-all inline-flex items-center gap-2 select-none border-0 shrink-0 self-start md:self-center"
            >
              View Roadmap
              <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>arrow_forward</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
