"use client";

import { useState, useEffect, useRef } from "react";

export default function BrainDumpModal({ open, onClose, onSubmit, loading }) {
  const [text, setText] = useState("");
  const textareaRef = useRef(null);

  useEffect(() => {
    if (open) {
      setText("");
      setTimeout(() => textareaRef.current?.focus(), 60);
    }
  }, [open]);

  if (!open) return null;

  const parsedTasks = text
    .split(/[\n,]+/)
    .map(t => t.trim())
    .filter(Boolean);

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!parsedTasks.length || loading) return;
    onSubmit(parsedTasks);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
      onClick={(e) => e.target === e.currentTarget && !loading && onClose()}
    >
      <div className="w-full max-w-lg bg-surface-container-lowest rounded-3xl border border-outline-variant/30 shadow-2xl overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-5 border-b border-outline-variant/20">
          <div className="flex items-center gap-2.5">
            <span
              className="material-symbols-outlined text-primary"
              style={{ fontSize: "22px", fontVariationSettings: "'FILL' 1" }}
            >
              inventory_2
            </span>
            <div>
              <h2 className="text-lg font-bold text-on-surface">Brain Dump</h2>
              <p className="text-xs text-on-surface-variant">One task per line, or separate with commas</p>
            </div>
          </div>
          <button
            onClick={onClose}
            disabled={loading}
            className="w-8 h-8 rounded-full flex items-center justify-center hover:bg-surface-container transition-colors disabled:opacity-40"
            aria-label="Close"
          >
            <span className="material-symbols-outlined text-on-surface-variant" style={{ fontSize: "20px" }}>close</span>
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="px-6 py-6 space-y-4">
          <textarea
            ref={textareaRef}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={"Buy groceries\nFinish Q3 report\nCall dentist, Schedule team sync"}
            rows={5}
            disabled={loading}
            className="w-full px-4 py-3 bg-surface-container rounded-xl border border-outline-variant/30 focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary/50 text-on-surface placeholder-on-surface-variant/40 transition-all resize-none font-mono text-sm disabled:opacity-50"
          />

          {/* Live preview chips */}
          {parsedTasks.length > 0 && (
            <div className="space-y-2">
              <p className="text-xs font-semibold text-on-surface-variant uppercase tracking-wide">
                {parsedTasks.length} task{parsedTasks.length !== 1 ? "s" : ""} detected
              </p>
              <div className="flex flex-wrap gap-2 max-h-28 overflow-y-auto">
                {parsedTasks.map((t, i) => (
                  <span
                    key={i}
                    className="inline-flex items-center gap-1 px-3 py-1 bg-primary/10 text-primary text-xs font-semibold rounded-full border border-primary/20"
                  >
                    <span className="material-symbols-outlined" style={{ fontSize: "11px" }}>add_task</span>
                    {t.length > 40 ? t.slice(0, 40) + "…" : t}
                  </span>
                ))}
              </div>
            </div>
          )}

          <div className="flex gap-3 pt-1">
            <button
              type="button"
              onClick={onClose}
              disabled={loading}
              className="flex-1 py-3 rounded-xl border border-outline-variant/40 text-on-surface-variant font-semibold hover:bg-surface-container transition-colors disabled:opacity-40"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading || parsedTasks.length === 0}
              className="flex-1 py-3 rounded-xl bg-primary text-on-primary font-bold hover:opacity-90 active:scale-95 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
            >
              {loading ? (
                <>
                  <span className="w-4 h-4 border-2 border-on-primary/30 border-t-on-primary rounded-full animate-spin" />
                  Saving…
                </>
              ) : (
                <>
                  <span
                    className="material-symbols-outlined"
                    style={{ fontSize: "18px", fontVariationSettings: "'FILL' 1" }}
                  >
                    rocket_launch
                  </span>
                  Dump {parsedTasks.length > 0 ? parsedTasks.length + " " : ""}Task{parsedTasks.length !== 1 ? "s" : ""}
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
