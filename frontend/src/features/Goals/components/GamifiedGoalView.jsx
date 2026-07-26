"use client";

import { useRouter } from 'next/navigation';

export default function GamifiedGoalView() {
  const router = useRouter();

  // In Next.js App Router there's no location.state; use defaults.
  const goal = {
    title: 'Master AI Engineering',
    pct: 45,
    dashOffset: 96,
    context: 'Professional',
    accentColor: '#0053dc',
    milestone: 'Complete Neural Networks Specialization',
  };

  const milestones = [
    { label: 'Market Research',                icon: 'analytics',      active: true,  locked: false, desc: 'Analyze the current landscape. Identify high-demand specializations like LLM architecture and RAG systems.', progress: 30 },
    { label: 'Neural Networks Specialization', icon: 'hub',            active: false, locked: true,  desc: 'Complete deep learning core curriculum. Unlock after concluding market positioning.', progress: 0 },
    { label: 'AI Portfolio Build',             icon: 'folder_special', active: false, locked: true,  desc: 'Construct three high-impact RAG applications to demonstrate expertise.', progress: 0 },
  ];

  return (
    <div className="max-w-6xl mx-auto p-10 pb-32">
      {/* Back */}
      <button className="flex items-center gap-1.5 text-sm font-semibold text-slate-400 hover:text-primary transition mb-10" onClick={() => router.push('/goals')}>
        <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>arrow_back</span>
        Back to Goals
      </button>

      {/* Goal Header */}
      <div className="mb-20 flex flex-col md:flex-row md:items-end justify-between gap-8">
        <div className="flex-1">
          <span className="text-xs font-bold uppercase tracking-widest mb-3 block" style={{ color: goal.accentColor }}>{goal.context || 'Professional'}</span>
          <h1 className="text-5xl md:text-6xl font-bold tracking-tight text-on-surface mb-6">{goal.title}</h1>
          <div className="inline-flex items-center gap-3 px-6 py-3 bg-white/80 backdrop-blur-md rounded-2xl shadow-sm">
            <span className="material-symbols-outlined text-primary" style={{ fontVariationSettings: "'FILL' 1" }}>auto_awesome</span>
            <p className="text-on-surface font-medium text-sm">✨ AI Insight: {goal.desc || `Tracking at ${goal.pct}% completion.`}</p>
          </div>
        </div>
        <div className="flex items-center gap-8 bg-white p-8 rounded-3xl shadow-sm min-w-[280px]">
          <div className="relative w-24 h-24">
            <svg className="w-full h-full transform -rotate-90">
              <circle cx="48" cy="48" fill="transparent" r="40" stroke="#f1f4f6" strokeWidth="10" />
              <circle cx="48" cy="48" fill="transparent" r="40" stroke={goal.accentColor || '#0053dc'} strokeDasharray="251.3" strokeDashoffset={251.3 * (1 - (goal.pct || 45) / 100)} strokeLinecap="round" strokeWidth="10" />
            </svg>
            <div className="absolute inset-0 flex items-center justify-center">
              <span className="text-xl font-bold text-on-surface">{goal.pct || 45}%</span>
            </div>
          </div>
          <div>
            <p className="text-on-surface-variant font-bold text-xs uppercase tracking-widest mb-1">{goal.context || 'Career Transition'}</p>
            <p className="text-lg font-bold text-on-surface">{goal.milestone || 'Next Milestone'}</p>
            <div className="mt-2 h-1.5 w-32 bg-surface-container-high rounded-full overflow-hidden">
              <div className="h-full rounded-full" style={{ width: `${goal.pct || 45}%`, background: goal.accentColor || '#0053dc' }} />
            </div>
          </div>
        </div>
      </div>

      {/* Interactive Journey Map */}
      <div className="relative pb-32">
        <div className="absolute left-1/2 top-0 bottom-0 w-[3px] -translate-x-1/2 z-0" style={{ background: `linear-gradient(to bottom,${goal.accentColor || '#0053dc'}33,transparent)` }} />
        <div className="relative z-10 flex flex-col gap-32">
          {milestones.map((m, i) => {
            const isLeft = i % 2 === 0;
            const cardClass = 'max-w-sm w-full p-8 rounded-3xl shadow-lg transition-transform duration-300 hover:-translate-y-1 relative overflow-hidden ' + (m.locked ? 'bg-white/40 backdrop-blur-md border border-white/50 grayscale opacity-60' : 'bg-white border-2');
            const cardStyle = !m.locked ? { borderColor: (goal.accentColor || '#0053dc') + '22' } : {};
            const card = (
              <div className={cardClass} style={cardStyle}>
                {!m.locked && (
                  <div className="absolute top-0 right-0 p-4 opacity-5">
                    <span className="material-symbols-outlined" style={{ fontSize: '80px' }}>{m.icon}</span>
                  </div>
                )}
                <div className="flex justify-between items-start mb-4">
                  <h3 className="text-xl font-bold text-on-surface">{m.label}</h3>
                  {m.active
                    ? <span className="text-[10px] font-bold px-3 py-1 rounded-full text-white" style={{ background: goal.accentColor || '#0053dc' }}>Active</span>
                    : <span className="material-symbols-outlined text-on-surface-variant">lock</span>
                  }
                </div>
                <p className="text-on-surface-variant text-sm leading-relaxed mb-6">{m.desc}</p>
                {m.active && (
                  <div className="h-2 w-full bg-surface-container-high rounded-full overflow-hidden">
                    <div className="h-full rounded-full" style={{ width: `${m.progress}%`, background: goal.accentColor || '#0053dc' }} />
                  </div>
                )}
              </div>
            );
            return (
              <div key={i} className="flex items-center justify-center w-full">
                {isLeft ? <div className="w-1/2 pr-16 flex justify-end">{card}</div> : <div className="w-1/2 pr-16" />}
                <div className="relative flex-shrink-0 flex items-center justify-center" style={{ width: '72px', height: '72px' }}>
                  {m.active ? (
                    <>
                      <div style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: goal.accentColor || '#0053dc', filter: 'blur(16px)', opacity: 0.25, animation: 'pulse 2s infinite' }} />
                      <div style={{ width: '56px', height: '56px', background: goal.accentColor || '#0053dc', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'white', boxShadow: '0 8px 24px rgba(0,0,0,0.15)', border: '4px solid white', zIndex: 1 }}>
                        <span className="material-symbols-outlined" style={{ fontSize: '24px', fontVariationSettings: "'FILL' 1" }}>{m.icon}</span>
                      </div>
                    </>
                  ) : (
                    <div style={{ width: '44px', height: '44px', background: '#f1f4f6', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid white', boxShadow: '0 2px 8px rgba(0,0,0,0.08)', zIndex: 1 }}>
                      <span className="material-symbols-outlined text-slate-400" style={{ fontSize: '18px' }}>lock</span>
                    </div>
                  )}
                </div>
                {!isLeft ? <div className="w-1/2 pl-16">{card}</div> : <div className="w-1/2 pl-16" />}
              </div>
            );
          })}
          <div className="flex items-center justify-center w-full mt-12 opacity-30">
            <div className="w-12 h-12 border-4 border-dashed border-outline-variant rounded-full flex items-center justify-center text-outline-variant">
              <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>emoji_events</span>
            </div>
          </div>
        </div>
      </div>

      {/* Floating Footer */}
      <div className="fixed bottom-10 left-1/2 -translate-x-1/2 z-40 pointer-events-none">
        <div style={{ background: 'rgba(255,255,255,0.85)', backdropFilter: 'blur(20px)' }} className="px-8 py-4 rounded-full flex items-center gap-10 shadow-2xl border border-white/40 pointer-events-auto">
          <div className="flex items-center gap-2">
            <div className="w-2.5 h-2.5 rounded-full" style={{ background: goal.accentColor || '#0053dc' }} />
            <span className="text-sm font-bold">1 Active Focus</span>
          </div>
          <div className="w-px h-4 bg-outline-variant/30" />
          <div className="flex items-center gap-2">
            <div className="w-2.5 h-2.5 rounded-full bg-outline-variant" />
            <span className="text-sm font-bold">{milestones.filter(m => m.locked).length} Milestones Ahead</span>
          </div>
          <div className="w-px h-4 bg-outline-variant/30" />
          <button className="text-sm font-bold flex items-center gap-1.5 text-primary hover:underline" onClick={() => router.push('/goals')}>
            <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>arrow_back</span> All Goals
          </button>
        </div>
      </div>
    </div>
  );
}
