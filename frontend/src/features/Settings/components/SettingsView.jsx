"use client";

import { useState } from 'react';

// ─── Reusable toggle ──────────────────────────────────────────────────────────
function Toggle({ id, checked, onChange, color = '#0053dc' }) {
  return (
    <button
      id={id}
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className="relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary/30"
      style={{ background: checked ? color : '#e2e8f0' }}
    >
      <span
        className="pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow-lg ring-0 transition duration-200 ease-in-out"
        style={{ transform: checked ? 'translateX(20px)' : 'translateX(0)' }}
      />
    </button>
  );
}

// ─── Section wrapper ─────────────────────────────────────────────────────────
function Section({ title, icon, children }) {
  return (
    <div className="bg-white rounded-3xl p-8 shadow-sm border border-white/50 space-y-6">
      <div className="flex items-center gap-3 pb-4 border-b border-slate-100">
        <div className="w-10 h-10 rounded-2xl bg-primary/8 flex items-center justify-center">
          <span className="material-symbols-outlined text-primary" style={{ fontSize: '20px', fontVariationSettings: "'FILL' 1" }}>{icon}</span>
        </div>
        <h2 className="text-lg font-bold text-on-surface tracking-tight">{title}</h2>
      </div>
      <div className="space-y-5">{children}</div>
    </div>
  );
}

// ─── Single setting row ───────────────────────────────────────────────────────
function SettingRow({ id, label, desc, checked, onChange, accentColor, badge }) {
  return (
    <div className="flex items-center justify-between gap-6 group">
      <div className="flex-1">
        <div className="flex items-center gap-2">
          <label htmlFor={id} className="text-sm font-semibold text-on-surface cursor-pointer">{label}</label>
          {badge && (
            <span className="text-[9px] font-bold uppercase tracking-widest px-2 py-0.5 rounded-full text-white" style={{ background: accentColor || '#0053dc' }}>{badge}</span>
          )}
        </div>
        {desc && <p className="text-xs text-on-surface-variant mt-0.5 leading-relaxed">{desc}</p>}
      </div>
      <Toggle id={id} checked={checked} onChange={onChange} color={accentColor} />
    </div>
  );
}

// ─── Slider row ───────────────────────────────────────────────────────────────
function SliderRow({ id, label, desc, value, onChange, min = 0, max = 100, step = 10, unit = '%', accentColor = '#0053dc' }) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <label htmlFor={id} className="text-sm font-semibold text-on-surface">{label}</label>
        <span className="text-xs font-bold tabular-nums" style={{ color: accentColor }}>{value}{unit}</span>
      </div>
      {desc && <p className="text-xs text-on-surface-variant leading-relaxed">{desc}</p>}
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={e => onChange(Number(e.target.value))}
        className="w-full h-1.5 rounded-full appearance-none cursor-pointer"
        style={{ accentColor }}
      />
      <div className="flex justify-between text-[9px] text-slate-400 font-medium">
        <span>Off</span>
        <span>Max</span>
      </div>
    </div>
  );
}

// ─── SettingsView ─────────────────────────────────────────────────────────────
export default function SettingsView() {
  // Appearance
  const [darkMode,        setDarkMode]        = useState(false);
  const [animationsOn,    setAnimationsOn]    = useState(true);
  const [accentAuto,      setAccentAuto]      = useState(true);

  // AI Coach
  const [aiCoachOn,       setAiCoachOn]       = useState(true);
  const [proactiveNudges, setProactiveNudges] = useState(true);
  const [aiStrictness,    setAiStrictness]    = useState(70);
  const [calendarAI,      setCalendarAI]      = useState(true);
  const [matrixAI,        setMatrixAI]        = useState(true);

  // Notifications
  const [pushNotifs,      setPushNotifs]      = useState(true);
  const [emailDigest,     setEmailDigest]     = useState(false);
  const [focusMode,       setFocusMode]       = useState(true);
  const [soundFX,         setSoundFX]         = useState(true);

  // Privacy & Data
  const [analyticsOn,     setAnalyticsOn]     = useState(true);
  const [cloudSync,       setCloudSync]       = useState(true);
  const [biometric,       setBiometric]       = useState(false);

  const [saved, setSaved] = useState(false);
  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <div className="max-w-3xl mx-auto p-10 pb-24 space-y-8">
      {/* Header */}
      <header className="mb-2">
        <span className="text-[10px] font-bold uppercase tracking-widest text-primary mb-2 block">Configuration</span>
        <h1 className="text-4xl font-bold tracking-tight text-on-surface">Settings</h1>
        <p className="text-on-surface-variant mt-1 text-sm">Customize your SperoFlow experience and AI behavior.</p>
      </header>

      {/* Appearance */}
      <Section title="Appearance" icon="palette">
        <SettingRow
          id="dark-mode"
          label="Dark Mode"
          desc="Switch to a darker color scheme to reduce eye strain at night."
          checked={darkMode}
          onChange={setDarkMode}
          accentColor="#1e293b"
        />
        <SettingRow
          id="animations"
          label="UI Animations"
          desc="Enable smooth micro-animations and view transitions."
          checked={animationsOn}
          onChange={setAnimationsOn}
        />
        <SettingRow
          id="accent-auto"
          label="Context-Aware Accent Color"
          desc="The accent color shifts to match the role or context of the active view."
          checked={accentAuto}
          onChange={setAccentAuto}
          badge="Beta"
          accentColor="#7c3aed"
        />
      </Section>

      {/* AI Coach */}
      <Section title="AI Coach" icon="smart_toy">
        <SettingRow
          id="ai-coach"
          label="Enable AI Coach"
          desc="Allow SperoFlow's AI to proactively analyze your tasks and journals."
          checked={aiCoachOn}
          onChange={setAiCoachOn}
          accentColor="#6366f1"
        />
        <SettingRow
          id="proactive-nudges"
          label="Proactive Nudges"
          desc="Receive unprompted insights when the AI detects patterns (e.g., decision fatigue)."
          checked={proactiveNudges}
          onChange={setProactiveNudges}
          accentColor="#6366f1"
          badge="AI"
        />
        <SliderRow
          id="ai-strictness"
          label="AI Strictness"
          desc="How aggressively the AI should intervene. Higher = more frequent suggestions."
          value={aiStrictness}
          onChange={setAiStrictness}
          accentColor="#6366f1"
        />
        <div className="grid grid-cols-2 gap-4 pt-2">
          <SettingRow id="calendar-ai" label="Calendar AI" desc="AI schedules gaps and focus blocks." checked={calendarAI} onChange={setCalendarAI} accentColor="#6366f1" />
          <SettingRow id="matrix-ai"   label="Matrix AI"   desc="Auto-sort new tasks into quadrants." checked={matrixAI}   onChange={setMatrixAI}   accentColor="#6366f1" />
        </div>
      </Section>

      {/* Notifications */}
      <Section title="Notifications" icon="notifications">
        <SettingRow id="push-notifs" label="Push Notifications" desc="Browser notifications for task deadlines and AI nudges." checked={pushNotifs} onChange={setPushNotifs} />
        <SettingRow id="email-digest" label="Weekly Email Digest" desc="Receive a Sunday morning summary of the week ahead." checked={emailDigest} onChange={setEmailDigest} accentColor="#006d4a" />
        <SettingRow id="focus-mode" label="Focus Mode (DND)" desc="Silence all notifications during Pomodoro / deep-work blocks." checked={focusMode} onChange={setFocusMode} accentColor="#865400" />
        <SettingRow id="sound-fx" label="Sound Effects" desc="Subtle sounds on task completion and AI interactions." checked={soundFX} onChange={setSoundFX} />
      </Section>

      {/* Privacy */}
      <Section title="Privacy & Data" icon="shield">
        <SettingRow id="analytics" label="Usage Analytics" desc="Anonymous telemetry to help improve SperoFlow. No personal data." checked={analyticsOn} onChange={setAnalyticsOn} accentColor="#006d4a" />
        <SettingRow id="cloud-sync" label="Cloud Sync" desc="Keep your data in sync across all devices." checked={cloudSync} onChange={setCloudSync} />
        <SettingRow id="biometric" label="Biometric Lock" desc="Require Face ID / fingerprint to open the app." checked={biometric} onChange={setBiometric} accentColor="#1e293b" badge="Soon" />
        <button className="text-xs font-bold text-error hover:underline mt-2">Delete all data…</button>
      </Section>

      {/* Sticky Save */}
      <div className="fixed bottom-8 left-1/2 -translate-x-1/2 z-50 pointer-events-none">
        <button
          onClick={handleSave}
          className="pointer-events-auto flex items-center gap-2 px-8 py-3 rounded-full font-bold text-sm text-white shadow-2xl transition-all duration-300"
          style={{
            background: saved ? '#006d4a' : 'linear-gradient(135deg,#0053dc,#6366f1)',
            boxShadow: saved ? '0 4px 24px rgba(0,109,74,0.35)' : '0 4px 24px rgba(0,83,220,0.35)',
          }}
        >
          <span className="material-symbols-outlined" style={{ fontSize: '18px', fontVariationSettings: "'FILL' 1" }}>
            {saved ? 'check_circle' : 'save'}
          </span>
          {saved ? 'Saved!' : 'Save Settings'}
        </button>
      </div>
    </div>
  );
}
