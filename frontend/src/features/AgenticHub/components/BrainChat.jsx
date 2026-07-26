"use client";

import { useEffect, useRef, useState } from "react";

import { ApiError, aiApi } from "@/lib/api/client";
import { ownedKnowledgeDatasetsApi } from "@/lib/api/owned-knowledge-datasets";

function answerFrom(result) {
  const payload = result?.payload || result?.data || result || {};
  const candidates = [payload.answer, payload.response, payload.message, payload.result?.answer, payload.summary];
  return candidates.find((value) => typeof value === "string" && value.trim()) || "I could not find a direct answer in the selected knowledge sources.";
}

function sourceLabel(source, index) {
  if (typeof source === "string") return source;
  return source?.citation || source?.title || source?.name || source?.source || `Source ${index + 1}`;
}

function messageFrom(error, fallback) {
  return error instanceof ApiError || error instanceof Error ? error.message : fallback;
}

export default function BrainChat({ defaultOpen = false }) {
  const [open, setOpen] = useState(defaultOpen);
  const [messages, setMessages] = useState([]);
  const [question, setQuestion] = useState("");
  const [loading, setLoading] = useState(false);
  const [loadingDatasets, setLoadingDatasets] = useState(false);
  const [datasets, setDatasets] = useState([]);
  const [selectedDatasetIds, setSelectedDatasetIds] = useState([]);
  const [datasetPickerOpen, setDatasetPickerOpen] = useState(false);
  const [error, setError] = useState(null);
  const scrollRef = useRef(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [messages, loading]);

  useEffect(() => {
    if (!open || datasets.length || loadingDatasets) return;
    let active = true;
    setLoadingDatasets(true);
    ownedKnowledgeDatasetsApi.list()
      .then((result) => {
        if (!active) return;
        setDatasets(Array.isArray(result) ? result : result?.items || []);
      })
      .catch(() => {
        // Roadmap chat stays available if the optional dataset list cannot load.
        if (active) setDatasets([]);
      })
      .finally(() => {
        if (active) setLoadingDatasets(false);
      });
    return () => { active = false; };
  }, [datasets.length, loadingDatasets, open]);

  const toggleDataset = (datasetId) => {
    setSelectedDatasetIds((current) => current.includes(datasetId)
      ? current.filter((id) => id !== datasetId)
      : [...current, datasetId]);
  };

  const send = async (event) => {
    event.preventDefault();
    const value = question.trim();
    if (!value || loading) return;

    const datasetScope = selectedDatasetIds.length > 0;
    setQuestion("");
    setError(null);
    setMessages((current) => [...current, {
      id: crypto.randomUUID(),
      role: "user",
      content: value,
      scope: datasetScope ? "Selected datasets" : "Roadmaps",
    }]);
    setLoading(true);

    try {
      const result = await aiApi.query({
        question: value,
        strategy: datasetScope ? "vector" : "hybrid",
        topK: 6,
        scope: datasetScope ? "dataset" : "roadmap",
        datasetIds: datasetScope ? selectedDatasetIds : [],
      });
      const payload = result?.payload || result?.data || result || {};
      const sources = Array.isArray(payload.citations)
        ? payload.citations
        : Array.isArray(payload.sources)
          ? payload.sources
          : [];
      setMessages((current) => [
        ...current,
        { id: crypto.randomUUID(), role: "assistant", content: answerFrom(result), sources },
      ]);
    } catch (requestError) {
      setError(messageFrom(requestError, "The knowledge assistant is unavailable right now."));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed bottom-4 right-4 z-[80] sm:bottom-6 sm:right-6">
      {open ? (
        <section aria-label="Knowledge assistant" className="mb-3 flex h-[34rem] w-[min(25rem,calc(100vw-2rem))] flex-col overflow-hidden rounded-lg border border-outline-variant/30 bg-white shadow-2xl">
          <header className="border-b border-outline-variant/20 px-4 py-3">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-primary" style={{ fontSize: "20px", fontVariationSettings: "'FILL' 1" }}>psychology</span>
                <div><h2 className="text-sm font-bold text-on-surface">Knowledge assistant</h2><p className="text-[11px] text-on-surface-variant">Private retrieval with source citations.</p></div>
              </div>
              <div className="flex items-center gap-1">
                <button aria-label="Start a new conversation" className="flex h-8 w-8 items-center justify-center rounded-md text-on-surface-variant hover:bg-surface-container" onClick={() => { setMessages([]); setError(null); }} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>refresh</span></button>
                <button aria-label="Close assistant" className="flex h-8 w-8 items-center justify-center rounded-md text-on-surface-variant hover:bg-surface-container" onClick={() => setOpen(false)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span></button>
              </div>
            </div>
            <div className="mt-3 rounded-md bg-surface-container-low px-2 py-1.5">
              <button aria-expanded={datasetPickerOpen} className="flex w-full items-center justify-between gap-2 text-left text-[11px] font-semibold text-on-surface-variant" onClick={() => setDatasetPickerOpen((current) => !current)} type="button">
                <span>{selectedDatasetIds.length ? `${selectedDatasetIds.length} private dataset${selectedDatasetIds.length === 1 ? "" : "s"} selected` : "Roadmap knowledge only"}</span>
                <span className="material-symbols-outlined" style={{ fontSize: "16px" }}>{datasetPickerOpen ? "expand_less" : "expand_more"}</span>
              </button>
              {datasetPickerOpen && <div className="mt-2 max-h-28 space-y-1 overflow-y-auto border-t border-outline-variant/20 pt-2">
                {loadingDatasets && <p className="px-1 py-1 text-[11px] text-on-surface-variant">Loading your datasets…</p>}
                {!loadingDatasets && datasets.length === 0 && <p className="px-1 py-1 text-[11px] text-on-surface-variant">No datasets are assigned to you.</p>}
                {datasets.map((dataset) => <label className="flex cursor-pointer items-center gap-2 rounded px-1 py-1 text-[11px] text-on-surface hover:bg-white" key={dataset.id}><input checked={selectedDatasetIds.includes(dataset.id)} onChange={() => toggleDataset(dataset.id)} type="checkbox" /><span className="min-w-0 truncate">{dataset.name}</span></label>)}
              </div>}
            </div>
          </header>

          <div className="flex-1 space-y-3 overflow-y-auto p-4" ref={scrollRef}>
            {messages.length === 0 && <div className="flex h-full flex-col items-center justify-center px-6 text-center"><span className="material-symbols-outlined text-primary/35" style={{ fontSize: "42px" }}>account_tree</span><p className="mt-3 text-sm font-semibold text-on-surface">Ask about your knowledge</p><p className="mt-1 text-xs leading-relaxed text-on-surface-variant">Select one or more assigned datasets to search them. Otherwise, your question uses the roadmap graph.</p></div>}
            {messages.map((message) => <article className={`max-w-[90%] rounded-lg px-3 py-2.5 text-sm leading-relaxed ${message.role === "user" ? "ml-auto bg-primary text-on-primary" : "border border-outline-variant/20 bg-surface-container-low text-on-surface"}`} key={message.id}><p className="whitespace-pre-wrap">{message.content}</p>{message.scope && <p className="mt-1 text-[10px] opacity-75">{message.scope}</p>}{message.sources?.length > 0 && <div className="mt-2 border-t border-outline-variant/20 pt-2 text-[11px] text-on-surface-variant">{message.sources.slice(0, 3).map((source, index) => <p key={`${sourceLabel(source, index)}-${index}`}>{sourceLabel(source, index)}</p>)}</div>}</article>)}
            {loading && <div className="flex w-fit items-center gap-2 rounded-lg border border-outline-variant/20 bg-surface-container-low px-3 py-2 text-xs font-bold text-on-surface-variant"><span className="material-symbols-outlined animate-spin" style={{ fontSize: "16px" }}>progress_activity</span>Searching</div>}
            {error && <p className="rounded-lg border border-error/20 bg-error/10 px-3 py-2 text-xs font-semibold text-error">{error}</p>}
          </div>

          <form className="flex gap-2 border-t border-outline-variant/20 p-3" onSubmit={send}>
            <label className="sr-only" htmlFor="brain-question">Question</label>
            <input className="min-w-0 flex-1 rounded-lg border border-outline-variant/40 bg-surface px-3 py-2 text-sm text-on-surface focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20" disabled={loading} id="brain-question" onChange={(event) => setQuestion(event.target.value)} placeholder={selectedDatasetIds.length ? "Ask the selected datasets" : "Ask a roadmap question"} value={question} />
            <button aria-label="Send question" className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-primary text-on-primary disabled:opacity-50" disabled={loading || !question.trim()} type="submit"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>arrow_upward</span></button>
          </form>
        </section>
      ) : null}

      <button aria-expanded={open} aria-label={open ? "Close knowledge assistant" : "Open knowledge assistant"} className="flex h-12 w-12 items-center justify-center rounded-full bg-primary text-on-primary shadow-lg transition-transform hover:scale-105" onClick={() => setOpen((current) => !current)} title={open ? "Close knowledge assistant" : "Open knowledge assistant"} type="button"><span className="material-symbols-outlined" style={{ fontSize: "23px", fontVariationSettings: "'FILL' 1" }}>{open ? "close" : "psychology"}</span></button>
    </div>
  );
}
