"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import { ApiError, goalsApi } from "@/lib/api/client";

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

export default function RoadmapView() {
  const router = useRouter();
  const [goals, setGoals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setGoals((await goalsApi.list()).filter((goal) => goal.state === "active"));
      setNotice(null);
    } catch (error) {
      setNotice(messageFrom(error, "Unable to load goals."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <header className="border-b border-outline-variant/20 pb-5">
        <p className="text-xs font-bold uppercase text-primary">Knowledge graph</p>
        <h1 className="mt-1 text-3xl font-bold text-on-surface">Roadmaps</h1>
        <p className="mt-2 text-sm text-on-surface-variant">Choose an active goal to review or generate a graph-grounded roadmap.</p>
      </header>
      {notice && <div className="mt-4 border border-error/20 bg-error/10 px-4 py-3 text-sm font-medium text-error">{notice}</div>}
      <section className="mt-6 border-t border-outline-variant/20">
        {loading ? <div className="h-48 animate-pulse bg-surface-container-low" /> : goals.length > 0 ? goals.map((goal) => <article className="flex items-center justify-between gap-4 border-b border-outline-variant/20 py-4" key={goal.id}><div className="min-w-0"><h2 className="truncate text-sm font-bold text-on-surface">{goal.title}</h2><p className="mt-1 text-xs text-on-surface-variant">{goal.totalMilestoneCount} milestones {goal.roadmapSummary ? "- roadmap ready" : ""}</p></div><button className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg text-primary hover:bg-primary/10" aria-label={"Open " + goal.title} onClick={() => router.push("/goals/" + goal.id)} title="Open goal" type="button"><span className="material-symbols-outlined">arrow_forward</span></button></article>) : <div className="py-12 text-center"><p className="text-sm text-on-surface-variant">Create an active goal before generating a roadmap.</p><button className="mt-4 rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary" onClick={() => router.push("/goals")} type="button">Open goals</button></div>}
      </section>
    </div>
  );
}