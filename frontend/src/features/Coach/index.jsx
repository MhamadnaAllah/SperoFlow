"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError, aiProposalsApi, coachApi } from "@/lib/api/client";

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

export default function CoachWorkspace() {
  const [conversations, setConversations] = useState([]);
  const [activeConversationId, setActiveConversationId] = useState(null);
  const [messages, setMessages] = useState([]);
  const [observations, setObservations] = useState([]);
  const [proposals, setProposals] = useState([]);
  const [inputMessage, setInputMessage] = useState("");
  const [newTitle, setNewTitle] = useState("");
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [convList, obsList, propList] = await Promise.all([
        coachApi.listConversations(),
        coachApi.listObservations(),
        aiProposalsApi.listPending(),
      ]);
      setConversations(convList);
      setObservations(obsList);
      setProposals(propList.filter((p) => p.source === "coach"));

      if (convList.length > 0 && !activeConversationId) {
        setActiveConversationId(convList[0].id);
      }
    } catch (err) {
      setError(messageFrom(err, "Failed to load Coach workspace."));
    } finally {
      setLoading(false);
    }
  }, [activeConversationId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const loadMessages = useCallback(async (convId) => {
    if (!convId) return;
    try {
      const msgList = await coachApi.listMessages(convId);
      setMessages(msgList);
    } catch (err) {
      setError(messageFrom(err, "Failed to load thread messages."));
    }
  }, []);

  useEffect(() => {
    if (activeConversationId) {
      loadMessages(activeConversationId);
    }
  }, [activeConversationId, loadMessages]);

  const handleCreateConversation = async (e) => {
    e.preventDefault();
    if (!newTitle.trim()) return;
    try {
      setError(null);
      const newConv = await coachApi.createConversation(newTitle.trim());
      setConversations((prev) => [newConv, ...prev]);
      setActiveConversationId(newConv.id);
      setNewTitle("");
    } catch (err) {
      setError(messageFrom(err, "Failed to create conversation thread."));
    }
  };

  const handleSendMessage = async (e) => {
    e.preventDefault();
    if (!inputMessage.trim() || !activeConversationId || sending) return;

    const userText = inputMessage.trim();
    setInputMessage("");
    setSending(true);
    setError(null);

    // Optimistic local add
    const tempUserMsg = {
      id: "temp-" + Date.now(),
      senderRole: "User",
      content: userText,
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, tempUserMsg]);

    try {
      const res = await coachApi.postMessage(activeConversationId, userText);
      setMessages((prev) =>
        prev.filter((m) => m.id !== tempUserMsg.id).concat([res.userMessage, res.coachMessage])
      );
      if (res.observations && res.observations.length > 0) {
        setObservations((prev) => [...res.observations, ...prev]);
      }
      if (res.proposals && res.proposals.length > 0) {
        setProposals((prev) => [...res.proposals, ...prev]);
      }
    } catch (err) {
      setError(messageFrom(err, "Failed to send message to Coach."));
    } finally {
      setSending(false);
    }
  };

  const handleDismissObservation = async (obsId) => {
    try {
      await coachApi.dismissObservation(obsId);
      setObservations((prev) => prev.filter((o) => o.id !== obsId));
    } catch (err) {
      setError(messageFrom(err, "Failed to dismiss observation."));
    }
  };

  const handleApproveProposal = async (proposalId) => {
    try {
      setError(null);
      await aiProposalsApi.approve(proposalId);
      setProposals((prev) => prev.filter((p) => p.id !== proposalId));
      setNotice("Proposal approved and applied to your workspace.");
      setTimeout(() => setNotice(null), 4000);
    } catch (err) {
      setError(messageFrom(err, "Failed to approve proposal."));
    }
  };

  const handleCancelProposal = async (proposalId) => {
    try {
      setError(null);
      await aiProposalsApi.cancel(proposalId);
      setProposals((prev) => prev.filter((p) => p.id !== proposalId));
    } catch (err) {
      setError(messageFrom(err, "Failed to cancel proposal."));
    }
  };

  if (loading) {
    return (
      <div className="flex h-96 items-center justify-center">
        <div className="flex items-center space-x-3 text-slate-500">
          <span className="material-symbols-outlined animate-spin text-2xl">sync</span>
          <span className="text-sm font-medium">Loading Coach Workspace...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      {/* Header */}
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4 border-b border-slate-200 pb-5 dark:border-slate-800">
        <div>
          <div className="flex items-center space-x-2">
            <span className="material-symbols-outlined text-3xl text-indigo-600 dark:text-indigo-400">
              psychology
            </span>
            <h1 className="text-2xl font-bold text-slate-900 dark:text-white">
              Personal Coach & Adviser
            </h1>
          </div>
          <p className="mt-1 text-sm text-slate-600 dark:text-slate-400">
            Covey-inspired guidance for Quadrant 2 alignment, habit creation, and balanced progress.
          </p>
        </div>
      </div>

      {/* Notifications */}
      {error && (
        <div className="mb-4 flex items-center justify-between rounded-lg bg-red-50 p-4 text-sm text-red-700 dark:bg-red-950/40 dark:text-red-300">
          <span>{error}</span>
          <button className="text-red-500 hover:text-red-700" onClick={() => setError(null)}>
            <span className="material-symbols-outlined text-sm">close</span>
          </button>
        </div>
      )}

      {notice && (
        <div className="mb-4 flex items-center justify-between rounded-lg bg-emerald-50 p-4 text-sm text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300">
          <span>{notice}</span>
          <button className="text-emerald-500 hover:text-emerald-700" onClick={() => setNotice(null)}>
            <span className="material-symbols-outlined text-sm">close</span>
          </button>
        </div>
      )}

      {/* Observations Banner */}
      {observations.length > 0 && (
        <div className="mb-6 rounded-xl border border-indigo-200 bg-indigo-50/50 p-4 dark:border-indigo-900/50 dark:bg-indigo-950/20">
          <div className="mb-3 flex items-center justify-between">
            <h3 className="flex items-center space-x-2 text-sm font-bold text-indigo-900 dark:text-indigo-300">
              <span className="material-symbols-outlined text-indigo-600 dark:text-indigo-400">
                insights
              </span>
              <span>Active Coach Observations</span>
            </h3>
            <span className="rounded-full bg-indigo-100 px-2.5 py-0.5 text-xs font-semibold text-indigo-800 dark:bg-indigo-900 dark:text-indigo-200">
              {observations.length}
            </span>
          </div>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {observations.map((obs) => (
              <div
                key={obs.id}
                className="flex flex-col justify-between rounded-lg border border-slate-200 bg-white p-3.5 shadow-sm dark:border-slate-800 dark:bg-slate-900"
              >
                <div>
                  <span className="inline-block rounded bg-indigo-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-indigo-800 dark:bg-indigo-950 dark:text-indigo-300">
                    {obs.scope}
                  </span>
                  <p className="mt-2 text-xs font-medium text-slate-700 dark:text-slate-300">
                    {obs.content}
                  </p>
                </div>
                <button
                  className="mt-3 text-right text-xs font-semibold text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
                  onClick={() => handleDismissObservation(obs.id)}
                >
                  Dismiss
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Main Workspace Layout */}
      <div className="grid gap-6 lg:grid-cols-12">
        {/* Left Column: Threads & Proposals */}
        <div className="space-y-6 lg:col-span-4">
          {/* Create Thread */}
          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-900">
            <h3 className="mb-3 text-sm font-bold text-slate-900 dark:text-white">
              Conversation Threads
            </h3>
            <form className="flex space-x-2" onSubmit={handleCreateConversation}>
              <input
                className="flex-1 rounded-lg border border-slate-300 px-3 py-2 text-xs text-slate-900 focus:border-indigo-500 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                onChange={(e) => setNewTitle(e.target.value)}
                placeholder="New topic (e.g. Weekly Q2 Review)..."
                value={newTitle}
              />
              <button
                className="rounded-lg bg-indigo-600 px-3 py-2 text-xs font-semibold text-white hover:bg-indigo-700"
                type="submit"
              >
                New
              </button>
            </form>

            <div className="mt-4 space-y-1.5 max-h-60 overflow-y-auto">
              {conversations.length === 0 ? (
                <p className="p-3 text-center text-xs text-slate-500">No conversations yet.</p>
              ) : (
                conversations.map((conv) => (
                  <button
                    key={conv.id}
                    className={`w-full rounded-lg px-3 py-2 text-left text-xs font-medium transition ${
                      activeConversationId === conv.id
                        ? "bg-indigo-50 text-indigo-700 dark:bg-indigo-950/60 dark:text-indigo-300"
                        : "text-slate-700 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800/50"
                    }`}
                    onClick={() => setActiveConversationId(conv.id)}
                  >
                    <div className="truncate font-semibold">{conv.title}</div>
                    <div className="text-[10px] text-slate-400">
                      {new Date(conv.updatedAt).toLocaleDateString()}
                    </div>
                  </button>
                ))
              )}
            </div>
          </div>

          {/* Pending Coach Proposals Drawer */}
          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-900">
            <div className="mb-3 flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-white">
                Coach Action Proposals
              </h3>
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-bold text-amber-800 dark:bg-amber-950 dark:text-amber-300">
                {proposals.length} Pending
              </span>
            </div>

            {proposals.length === 0 ? (
              <p className="py-4 text-center text-xs text-slate-400">
                No pending proposals from Coach.
              </p>
            ) : (
              <div className="space-y-3">
                {proposals.map((prop) => (
                  <div
                    key={prop.id}
                    className="rounded-lg border border-amber-200 bg-amber-50/40 p-3 dark:border-amber-900/40 dark:bg-amber-950/20"
                  >
                    <span className="rounded bg-amber-200/60 px-1.5 py-0.5 text-[10px] font-bold text-amber-900 dark:bg-amber-900 dark:text-amber-200">
                      {prop.kind}
                    </span>
                    <h4 className="mt-1.5 text-xs font-bold text-slate-900 dark:text-white">
                      {prop.title}
                    </h4>
                    <p className="mt-1 text-[11px] text-slate-600 dark:text-slate-400">
                      {prop.description}
                    </p>
                    <div className="mt-3 flex justify-end space-x-2">
                      <button
                        className="rounded bg-slate-200 px-2.5 py-1 text-[11px] font-semibold text-slate-700 hover:bg-slate-300 dark:bg-slate-800 dark:text-slate-300"
                        onClick={() => handleCancelProposal(prop.id)}
                      >
                        Cancel
                      </button>
                      <button
                        className="rounded bg-indigo-600 px-2.5 py-1 text-[11px] font-semibold text-white hover:bg-indigo-700"
                        onClick={() => handleApproveProposal(prop.id)}
                      >
                        Approve
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Right Column: Active Conversation */}
        <div className="flex flex-col rounded-xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900 lg:col-span-8 h-[600px]">
          {/* Chat Messages Header */}
          <div className="border-b border-slate-200 px-5 py-4 dark:border-slate-800">
            <h3 className="font-bold text-slate-900 dark:text-white">
              {conversations.find((c) => c.id === activeConversationId)?.title || "Coach Discussion"}
            </h3>
          </div>

          {/* Chat Stream */}
          <div className="flex-1 overflow-y-auto p-5 space-y-4">
            {messages.length === 0 ? (
              <div className="flex h-full items-center justify-center text-center">
                <div className="max-w-sm space-y-2">
                  <span className="material-symbols-outlined text-4xl text-slate-300 dark:text-slate-700">
                    chat_bubble_outline
                  </span>
                  <p className="text-xs text-slate-500">
                    Ask your Coach for advice on Quadrant 2 planning, role balance, habit consistency, or focus scheduling.
                  </p>
                </div>
              </div>
            ) : (
              messages.map((msg) => {
                const isCoach = msg.senderRole === "Coach";
                return (
                  <div
                    key={msg.id}
                    className={`flex ${isCoach ? "justify-start" : "justify-end"}`}
                  >
                    <div
                      className={`max-w-lg rounded-2xl px-4 py-3 text-xs leading-relaxed ${
                        isCoach
                          ? "bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200"
                          : "bg-indigo-600 text-white"
                      }`}
                    >
                      <div className="mb-1 flex items-center justify-between text-[10px] opacity-75">
                        <span className="font-bold">{isCoach ? "Coach" : "You"}</span>
                        <span>{new Date(msg.createdAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</span>
                      </div>
                      <p className="whitespace-pre-wrap">{msg.content}</p>
                    </div>
                  </div>
                );
              })
            )}
          </div>

          {/* Message Input */}
          <form className="border-t border-slate-200 p-4 dark:border-slate-800" onSubmit={handleSendMessage}>
            <div className="flex space-x-3">
              <input
                className="flex-1 rounded-lg border border-slate-300 px-4 py-2.5 text-xs text-slate-900 focus:border-indigo-500 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-white"
                disabled={!activeConversationId || sending}
                onChange={(e) => setInputMessage(e.target.value)}
                placeholder="Type your message to Coach..."
                value={inputMessage}
              />
              <button
                className="flex items-center space-x-1 rounded-lg bg-indigo-600 px-4 py-2.5 text-xs font-semibold text-white transition hover:bg-indigo-700 disabled:opacity-50"
                disabled={!activeConversationId || !inputMessage.trim() || sending}
                type="submit"
              >
                {sending ? (
                  <span className="material-symbols-outlined animate-spin text-sm">sync</span>
                ) : (
                  <>
                    <span>Send</span>
                    <span className="material-symbols-outlined text-sm">send</span>
                  </>
                )}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}
