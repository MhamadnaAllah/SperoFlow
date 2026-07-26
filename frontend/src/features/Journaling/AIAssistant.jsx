"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import ErrorBoundary from "@/components/ui/ErrorBoundary";
import { aiProposalsApi, ApiError, journalApi } from "@/lib/api/client";

const MOODS = ["Good", "Calm", "Neutral", "Low", "Tired"];
const DEFAULT_MOOD = "Neutral";

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function formatDate(value) {
  return new Date(value).toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

function pendingReviewMap(analyses) {
  return Object.fromEntries(
    analyses
      .filter((analysis) => analysis.proposal.state === "pending")
      .map((analysis) => [analysis.journalEntryId, analysis]),
  );
}

function ApprovedInsight({ insight }) {
  if (!insight || insight.state !== "approved") return null;
  return <aside className="mt-4 border-t border-primary/15 pt-3" aria-label="Approved reflection">
    <div className="flex flex-wrap items-center gap-2">
      <span className="material-symbols-outlined text-primary" style={{ fontSize: "17px", fontVariationSettings: "'FILL' 1" }}>auto_awesome</span>
      <span className="text-xs font-bold text-primary">Reflection kept with this entry</span>
      {insight.emotions.map((emotion) => <span className="rounded-md bg-primary/10 px-2 py-1 text-[11px] font-bold text-primary" key={emotion}>{emotion}</span>)}
    </div>
    <p className="mt-2 text-sm leading-relaxed text-on-surface">{insight.feedback}</p>
    <p className="mt-2 text-xs leading-relaxed text-on-surface-variant">{insight.progressSummary}</p>
  </aside>;
}

function EntryCard({ entry }) {
  const content = entry.content || "";
  const snippet = content.length > 300 ? `${content.slice(0, 300).trimEnd()}...` : content;
  return (
    <article className="rounded-lg border border-outline-variant/20 bg-white p-4 shadow-sm">
      <div className="mb-2 flex items-center justify-between gap-3">
        <time className="text-xs font-semibold text-on-surface-variant" dateTime={entry.createdAt}>
          {formatDate(entry.createdAt)}
        </time>
        <span className="rounded-md bg-secondary/10 px-2 py-1 text-[11px] font-bold text-secondary">{entry.mood || DEFAULT_MOOD}</span>
      </div>
      <p className="whitespace-pre-wrap font-serif text-sm leading-relaxed text-on-surface">{snippet}</p>
      <ApprovedInsight insight={entry.insight} />
    </article>
  );
}

function ReviewCard({ analysis, entry, busy, onApprove, onCancel }) {
  const { insight } = analysis;
  return <article className="rounded-lg border border-primary/25 bg-primary/5 p-4 shadow-sm">
    <div className="flex items-start justify-between gap-3">
      <div className="flex items-center gap-2">
        <span className="material-symbols-outlined text-primary" style={{ fontSize: "20px", fontVariationSettings: "'FILL' 1" }}>auto_awesome</span>
        <div><h3 className="text-sm font-bold text-on-surface">Reflection to review</h3><p className="text-xs text-on-surface-variant">{entry ? formatDate(entry.createdAt) : "Saved journal entry"}</p></div>
      </div>
      <div className="flex flex-wrap justify-end gap-1.5">{insight.emotions.map((emotion) => <span className="rounded-md bg-white px-2 py-1 text-[11px] font-bold text-primary" key={emotion}>{emotion}</span>)}</div>
    </div>
    <p className="mt-3 text-sm leading-relaxed text-on-surface">{insight.feedback}</p>
    <p className="mt-2 text-xs leading-relaxed text-on-surface-variant">{insight.progressSummary}</p>
    <div className="mt-4 flex flex-wrap justify-end gap-2">
      <button aria-busy={busy} className="rounded-lg border border-outline-variant/40 px-3 py-2 text-xs font-bold text-on-surface-variant hover:bg-white disabled:opacity-50" disabled={busy} onClick={() => onCancel(analysis)} type="button">Cancel</button>
      <button aria-busy={busy} className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-xs font-bold text-on-primary disabled:opacity-50" disabled={busy} onClick={() => onApprove(analysis)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "16px" }}>{busy ? "progress_activity" : "check"}</span>Keep reflection</button>
    </div>
  </article>;
}

export default function AIAssistant() {
  return (
    <ErrorBoundary>
      <JournalWorkspace />
    </ErrorBoundary>
  );
}

function JournalWorkspace() {
  const [content, setContent] = useState("");
  const [mood, setMood] = useState(DEFAULT_MOOD);
  const [entries, setEntries] = useState([]);
  const [pendingReviews, setPendingReviews] = useState({});
  const [analyzingEntryIds, setAnalyzingEntryIds] = useState(() => new Set());
  const [reviewActionId, setReviewActionId] = useState(null);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [historyOpen, setHistoryOpen] = useState(true);
  const textareaRef = useRef(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [journalEntries, analyses] = await Promise.all([journalApi.list(), journalApi.listPendingAnalyses()]);
      setEntries(journalEntries);
      setPendingReviews(pendingReviewMap(analyses));
      setError(null);
    } catch (requestError) {
      setError(messageFrom(requestError, "Unable to load your reflections."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const requestAnalysis = useCallback(async (entry) => {
    setAnalyzingEntryIds((current) => new Set(current).add(entry.id));
    try {
      const analysis = await journalApi.analyze(entry.id);
      if (analysis.proposal.state === "pending") {
        setPendingReviews((current) => ({ ...current, [entry.id]: analysis }));
      } else if (analysis.insight.state === "approved") {
        setEntries((current) => current.map((item) => item.id === entry.id ? { ...item, insight: analysis.insight } : item));
      }
    } catch (requestError) {
      setError(messageFrom(requestError, "Your reflection was saved, but its review is unavailable right now."));
    } finally {
      setAnalyzingEntryIds((current) => {
        const next = new Set(current);
        next.delete(entry.id);
        return next;
      });
    }
  }, []);

  const filteredEntries = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase();
    if (!normalized) return entries;
    return entries.filter((entry) => `${entry.content} ${entry.mood || ""}`.toLocaleLowerCase().includes(normalized));
  }, [entries, query]);

  const reviews = useMemo(() => Object.values(pendingReviews), [pendingReviews]);
  const entriesById = useMemo(() => new Map(entries.map((entry) => [entry.id, entry])), [entries]);

  const save = async () => {
    const trimmed = content.trim();
    if (!trimmed || saving) return;
    setSaving(true);
    try {
      const entry = await journalApi.create({ content: trimmed, mood });
      setEntries((current) => [entry, ...current]);
      setContent("");
      setMood(DEFAULT_MOOD);
      setError(null);
      textareaRef.current?.focus();
      void requestAnalysis(entry);
    } catch (requestError) {
      setError(messageFrom(requestError, "Your reflection could not be saved."));
    } finally {
      setSaving(false);
    }
  };

  const resolveReview = async (analysis, action) => {
    if (reviewActionId) return;
    setReviewActionId(analysis.proposal.id);
    try {
      const proposal = action === "approve"
        ? await aiProposalsApi.approve(analysis.proposal.id, analysis.proposal.concurrencyToken)
        : await aiProposalsApi.cancel(analysis.proposal.id, analysis.proposal.concurrencyToken);
      setPendingReviews((current) => {
        const next = { ...current };
        delete next[analysis.journalEntryId];
        return next;
      });
      if (proposal.state === "approved") {
        const approvedInsight = { ...analysis.insight, state: "approved", resolvedAt: proposal.resolvedAt };
        setEntries((current) => current.map((entry) => entry.id === analysis.journalEntryId ? { ...entry, insight: approvedInsight } : entry));
      }
      setError(null);
    } catch (requestError) {
      setError(messageFrom(requestError, "That reflection could not be updated. Refresh and try again."));
    } finally {
      setReviewActionId(null);
    }
  };

  const wordCount = content.trim() ? content.trim().split(/\s+/).length : 0;

  return <div className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
    <header className="mb-6 border-b border-outline-variant/20 pb-5">
      <p className="text-xs font-bold uppercase text-secondary">Reflection</p>
      <h1 className="mt-1 text-3xl font-bold text-on-surface">Journal</h1>
      <p className="mt-2 text-sm text-on-surface-variant">A private space to write, review, and notice patterns over time.</p>
    </header>

    {error && <div className="mb-5 flex items-center justify-between gap-3 rounded-lg border border-error/20 bg-error/10 px-4 py-3 text-sm font-medium text-error"><span>{error}</span><button className="text-xs font-bold underline" onClick={() => setError(null)} type="button">Dismiss</button></div>}

    <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1.15fr)_minmax(20rem,0.85fr)]">
      <section className="overflow-hidden rounded-lg border border-outline-variant/25 bg-white shadow-sm">
        <div className="flex items-center gap-3 border-b border-outline-variant/20 px-5 py-4">
          <span className="material-symbols-outlined text-secondary" style={{ fontSize: "21px", fontVariationSettings: "'FILL' 1" }}>edit_note</span>
          <div><h2 className="text-sm font-bold text-on-surface">Today's reflection</h2><p className="text-xs text-on-surface-variant">Write plainly. Your entries remain in your account.</p></div>
        </div>
        <div className="border-b border-outline-variant/15 px-5 py-3"><div aria-label="Mood" className="flex flex-wrap gap-2">{MOODS.map((item) => <button aria-pressed={mood === item} className={`rounded-lg border px-3 py-1.5 text-xs font-bold ${mood === item ? "border-secondary bg-secondary text-on-secondary" : "border-outline-variant/35 text-on-surface-variant hover:bg-surface-container"}`} key={item} onClick={() => setMood(item)} type="button">{item}</button>)}</div></div>
        <div className="p-5"><textarea aria-label="Journal reflection" className="min-h-[20rem] w-full resize-y border-0 bg-transparent font-serif text-base leading-relaxed text-on-surface placeholder:text-on-surface-variant/55 focus:outline-none" onChange={(event) => setContent(event.target.value)} placeholder="What is on your mind today?" ref={textareaRef} value={content} /></div>
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-outline-variant/15 bg-surface-container-low px-5 py-3"><div className="text-xs text-on-surface-variant">{wordCount ? `${wordCount} words` : "Start writing"}</div><div className="flex items-center gap-3">{content && <button className="text-xs font-bold text-on-surface-variant hover:text-error" onClick={() => setContent("")} type="button">Clear</button>}<button aria-busy={saving} className="flex items-center gap-2 rounded-lg bg-secondary px-4 py-2 text-sm font-bold text-on-secondary disabled:opacity-50" disabled={saving || !content.trim()} onClick={save} type="button"><span className="material-symbols-outlined" style={{ fontSize: "17px" }}>{saving ? "progress_activity" : "save"}</span>{saving ? "Saving" : "Save reflection"}</button></div></div>
      </section>

      <section className="overflow-hidden rounded-lg border border-outline-variant/25 bg-white shadow-sm">
        <div className="border-b border-outline-variant/20 px-5 py-4"><div className="flex items-center gap-3"><span className="material-symbols-outlined text-primary" style={{ fontSize: "21px", fontVariationSettings: "'FILL' 1" }}>history_edu</span><div><h2 className="text-sm font-bold text-on-surface">Your reflections</h2><p className="text-xs text-on-surface-variant">Search the entries saved in this workspace.</p></div></div><label className="relative mt-4 block"><span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant" style={{ fontSize: "18px" }}>search</span><input className="w-full rounded-lg border border-outline-variant/40 bg-surface py-2.5 pl-10 pr-3 text-sm text-on-surface focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20" onChange={(event) => setQuery(event.target.value)} placeholder="Search your writing" type="search" value={query} /></label></div>
        <div className="max-h-[33rem] space-y-3 overflow-y-auto p-4">{loading && [0, 1, 2].map((item) => <div className="h-28 animate-pulse rounded-lg bg-surface-container" key={item} />)}{!loading && filteredEntries.map((entry) => <EntryCard entry={entry} key={entry.id} />)}{!loading && filteredEntries.length === 0 && <div className="px-4 py-16 text-center"><span className="material-symbols-outlined text-on-surface-variant/35" style={{ fontSize: "40px" }}>auto_stories</span><p className="mt-2 text-sm font-semibold text-on-surface-variant">{query ? "No matching reflections" : "No reflections yet"}</p></div>}</div>
      </section>
    </div>

    {(reviews.length > 0 || analyzingEntryIds.size > 0) && <section className="mt-6 border-t border-outline-variant/20 pt-5">
      <div className="mb-3 flex items-center gap-2"><span className="material-symbols-outlined text-primary" style={{ fontSize: "19px" }}>rate_review</span><h2 className="text-sm font-bold text-on-surface">Reflections to review</h2></div>
      {analyzingEntryIds.size > 0 && <p className="mb-3 text-xs text-on-surface-variant">Preparing a reflection for your latest entry.</p>}
      <div className="grid gap-3 lg:grid-cols-2">{reviews.map((analysis) => <ReviewCard analysis={analysis} busy={reviewActionId === analysis.proposal.id} entry={entriesById.get(analysis.journalEntryId)} key={analysis.proposal.id} onApprove={(value) => resolveReview(value, "approve")} onCancel={(value) => resolveReview(value, "cancel")} />)}</div>
    </section>}

    <section className="mt-6 border-t border-outline-variant/20 pt-4"><button aria-expanded={historyOpen} className="flex items-center gap-2 text-sm font-bold text-on-surface" onClick={() => setHistoryOpen((current) => !current)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>{historyOpen ? "expand_less" : "expand_more"}</span>{entries.length} saved reflection{entries.length === 1 ? "" : "s"}</button>{historyOpen && !loading && <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">{entries.map((entry) => <EntryCard entry={entry} key={entry.id} />)}</div>}</section>
  </div>;
}