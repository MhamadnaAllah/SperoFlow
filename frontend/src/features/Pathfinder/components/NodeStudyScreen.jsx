"use client";

import { useState } from "react";

// Built-in glossary definition mapping for interactive Term Explainer
const DEFINITIONS = {
  "TCP/IP": {
    term: "TCP/IP Suite",
    category: "Transport & Network",
    desc: "Transmission Control Protocol (TCP) and Internet Protocol (IP) constitute the fundamental routing suite of the internet. IP targets and routes individual coordinate packets, while TCP sets dynamic ports, handles chronological segment reassembly, and guarantees delivery with ACK handshakes.",
  },
  "DNS (Domain Name System)": {
    term: "DNS (Domain Name System)",
    category: "Lookup Infrastructure",
    desc: "An hierarchical decentralized naming index that translates human-readable addresses (like example.com) to machine-actionable IP addresses (like 192.168.1.1 or IPv6 indexes).",
  },
  DNS: {
    term: "Domain Name System",
    category: "Lookup Infrastructure",
    desc: "An hierarchical decentralized naming index that translates human-readable addresses (like example.com) to machine-actionable IP addresses.",
  },
  "HTTP/HTTPS": {
    term: "HTTP/HTTPS Protocols",
    category: "Application Layer",
    desc: "Hypertext Transfer Protocol (HTTP) handles hypertext transfers on the web. Hypertext Transfer Protocol Secure (HTTPS) runs encrypted with Transport Layer Security (TLS), preventing eavesdropping or injection.",
  },
  "DOM Tree": {
    term: "Document Object Model (DOM)",
    category: "Frontend Layer",
    desc: "The programmatic API interface for HTML/XML document models, representing elements as nodes in a tree structure that browsers parse and compile into render models.",
  },
  Git: {
    term: "Git Core Versioning",
    category: "Version Control",
    desc: "A distributed system for source code tracking. Git maintains directories using hash tree records, permitting concurrent branches, cherry-picks, and robust merge conflicts management.",
  },
  RAG: {
    term: "Retrieval-Augmented Generation (RAG)",
    category: "Artificial Intelligence",
    desc: "A technique that supplies LLMs with relevant prompt documents queried from indexes based on semantic proximity, improving claim accuracy and preventing hallucinations.",
  },
  "Vector DBs": {
    term: "Vector Databases & Coordinates",
    category: "Data Indexes",
    desc: "Core systems optimized for coordinate distance lookup (such as cosine or euclidean distance metrics) across high-dimensional float vectors computed by text embedding models.",
  },
};

export default function NodeStudyScreen({ module, onNavigateBack, onUpdateModule }) {
  const [isFullscreenGraph, setIsFullscreenGraph] = useState(false);
  const [activeAccordionIndex, setActiveAccordionIndex] = useState(0);
  const [activeGlossaryTerm, setActiveGlossaryTerm] = useState(null);
  const [activeCitationSegment, setActiveCitationSegment] = useState(null);

  // Saved Notes Pad State
  const [showingNotePad, setShowingNotePad] = useState(false);
  const [noteContent, setNoteContent] = useState("");
  const [savedNotesList, setSavedNotesList] = useState({});

  // Toggle tasks and recalculate progress metric
  const handleToggleTask = (taskId) => {
    const updatedTasks = module.tasks.map((task) =>
      task.id === taskId ? { ...task, completed: !task.completed } : task
    );

    const completedCount = updatedTasks.filter((t) => t.completed).length;
    const newProgress = Math.round((completedCount / updatedTasks.length) * 100);

    onUpdateModule({
      ...module,
      tasks: updatedTasks,
      progress: newProgress,
      status: newProgress === 100 ? "completed" : "progress",
      category: newProgress === 100 ? "Core Module" : "Current Focus",
    });
  };

  // Click on any word in Insight to check glossary definitions
  const handleWordClick = (text) => {
    const cleanWord = text.replace(/[(),.;]/g, "").trim();
    if (DEFINITIONS[cleanWord]) {
      setActiveGlossaryTerm(DEFINITIONS[cleanWord]);
    } else {
      const matchedKey = Object.keys(DEFINITIONS).find(
        (key) =>
          cleanWord.toLowerCase().includes(key.toLowerCase()) ||
          key.toLowerCase().includes(cleanWord.toLowerCase())
      );
      if (matchedKey) {
        setActiveGlossaryTerm(DEFINITIONS[matchedKey]);
      }
    }
  };

  // Complete module in one click
  const handleConquerModule = () => {
    const allCompleted = module.tasks.map((t) => ({ ...t, completed: true }));
    onUpdateModule({
      ...module,
      tasks: allCompleted,
      progress: 100,
      status: "completed",
      category: "Core Module",
    });
  };

  // Save personal study notes
  const handleSaveNotes = () => {
    setSavedNotesList({
      ...savedNotesList,
      [module.id]: noteContent,
    });
    setShowingNotePad(false);
  };

  const handleOpenNotePad = () => {
    setNoteContent(savedNotesList[module.id] || "");
    setShowingNotePad(true);
  };

  return (
    <div className="space-y-8 animate-fade-in relative">
      {/* Breadcrumb row */}
      <nav className="flex items-center gap-2 text-on-surface-variant font-semibold text-[11px] select-none">
        <button
          onClick={onNavigateBack}
          className="hover:text-primary transition-colors hover:underline cursor-pointer bg-transparent border-0"
        >
          Roadmap
        </button>
        <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>chevron_right</span>
        <span className="text-on-surface-variant/70">Core Concepts</span>
        <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>chevron_right</span>
        <span className="text-primary font-bold">{module.title}</span>
      </nav>

      {/* Hero Page Title and Badge Row */}
      <header className="flex flex-col md:flex-row md:items-end justify-between gap-6 border-b border-outline-variant/40 pb-6">
        <div className="space-y-3">
          <div className="flex items-center gap-3">
            <span
              className={`text-[10px] font-mono font-bold px-2 py-0.5 rounded uppercase tracking-wider border flex items-center gap-1 ${
                module.status === "completed"
                  ? "bg-status-completed/10 text-status-completed border-status-completed/20"
                  : "bg-primary/10 text-primary border-primary/20"
              }`}
            >
              <span className="material-symbols-outlined" style={{ fontSize: '12px' }}>
                {module.status === "completed" ? "check_circle" : "sync"}
              </span>
              {module.status === "completed" ? "Completed" : "In Progress"}
            </span>
            <span className="text-[11px] font-mono font-bold text-on-surface-variant bg-surface-container px-2 py-0.5 rounded border border-outline-variant/30">
              Node ID: {module.id}
            </span>
          </div>

          <h1
            id="node-study-title"
            className="text-3xl md:text-4xl font-headline font-extrabold text-on-surface tracking-tight"
          >
            {module.title}
          </h1>
          <p className="text-sm text-on-surface-variant max-w-3xl leading-relaxed">
            {module.synthesizedInsight
              ? module.synthesizedInsight.slice(0, 180) + "..."
              : "Explore core topologies, RAG documents, and study guides below."}
            {" "}Analyze core topologies, RAG documents, and study guides below.
          </p>
        </div>

        {/* Action button panel */}
        <div className="flex items-center gap-3 shrink-0">
          <span className="text-[10px] font-mono font-bold text-outline flex items-center gap-1 max-md:hidden">
            <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>update</span>
            Synced: 2h ago
          </span>
          <button
            onClick={handleOpenNotePad}
            className="bg-surface-container-high hover:bg-surface-container-highest border border-outline-variant text-on-surface px-4 py-2 rounded-lg font-bold text-[11px] flex items-center gap-2 shadow-sm transition-all select-none hover:-translate-y-0.5"
          >
            <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>
              {savedNotesList[module.id] ? "edit_note" : "bookmark"}
            </span>
            {savedNotesList[module.id] ? "Review Notes" : "Save to Notes"}
          </button>
        </div>
      </header>

      {/* Main Grid Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Left Side (Span 2) */}
        <div className="lg:col-span-2 space-y-8">
          {/* Entity Topology Visualizer */}
          <section className="bg-surface-container-low border border-outline-variant rounded-xl overflow-hidden relative">
            <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent pointer-events-none" />

            <div className="p-4 border-b border-outline-variant/50 flex items-center justify-between bg-surface-container/50">
              <h2 className="font-headline font-bold text-base text-on-surface flex items-center gap-2">
                <span className="material-symbols-outlined text-primary" style={{ fontSize: '20px' }}>device_hub</span>
                Entity Topology
              </h2>
              <button
                onClick={() => setIsFullscreenGraph(!isFullscreenGraph)}
                className="text-on-surface-variant hover:text-primary transition-colors p-1 hover:bg-surface-container rounded-full select-none"
              >
                <span className="material-symbols-outlined" style={{ fontSize: '20px' }}>fullscreen</span>
              </button>
            </div>

            {/* High visual complexity node map placeholder */}
            <div className="relative h-64 sm:h-80 w-full bg-[#060e20] flex items-center justify-center overflow-hidden">
              <div className="absolute inset-0 bg-gradient-to-br from-primary/10 via-transparent to-status-completed/10" />
              <div className="text-center z-10">
                <span className="material-symbols-outlined text-primary/40" style={{ fontSize: '64px' }}>device_hub</span>
                <p className="text-primary-fixed/60 text-[11px] font-mono font-bold mt-2">Knowledge Graph Topology</p>
              </div>
              {/* Overlay Statistics Indicators */}
              <div className="absolute top-4 left-4 bg-[#0a142c]/95 backdrop-blur-md border border-[#213763] p-3 rounded-lg font-mono text-[10px] text-primary-fixed leading-normal shadow-xl">
                <div className="flex justify-between items-center gap-4">
                  <span>Topology States:</span>
                  <span className="text-status-completed font-bold">ONLINE</span>
                </div>
                <div className="mt-1 flex justify-between items-center gap-4">
                  <span>Nodes Loaded:</span>
                  <span className="text-white font-bold">14 Entities</span>
                </div>
                <div className="mt-0.5 flex justify-between items-center gap-4">
                  <span>Connections:</span>
                  <span className="text-white font-bold">32 Edges</span>
                </div>
              </div>
            </div>
          </section>

          {/* RAG Synthesized Insight */}
          <section className="bg-surface-container border border-outline-variant rounded-xl p-6 shadow-sm">
            <div className="flex items-center gap-2.5 mb-4">
              <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center">
                <span className="material-symbols-outlined text-primary" style={{ fontSize: '18px' }}>auto_awesome</span>
              </div>
              <h2 className="font-headline font-bold text-lg text-on-surface">Synthesized Insight</h2>
            </div>

            {/* Split paragraphs to make specific terms clickable */}
            <div className="text-on-surface-variant leading-relaxed text-sm font-headline font-medium space-y-4">
              <p>
                {(module.synthesizedInsight || "").split(" ").map((word, idx) => {
                  const plain = word.replace(/[(),.;]/g, "");
                  const isInteractive = [
                    "TCP/IP",
                    "DNS",
                    "HTTP/HTTPS",
                    "DOM",
                    "Git",
                    "RAG",
                    "Vector",
                  ].some((term) => plain.includes(term));
                  return (
                    <span key={idx}>
                      {isInteractive ? (
                        <button
                          onClick={() => handleWordClick(plain)}
                          className="text-primary font-bold bg-primary-fixed/20 hover:bg-primary-fixed/35 border border-primary/20 px-1 rounded transition-colors inline-block select-none focus:outline-none"
                        >
                          {word}
                        </button>
                      ) : (
                        word
                      )}
                      {" "}
                    </span>
                  );
                })}
              </p>
            </div>

            {/* Simulated Citation Labels */}
            <div className="mt-6 pt-4 border-t border-outline-variant/40 flex flex-wrap gap-2 text-[11px]">
              <span className="text-outline font-headline font-bold uppercase tracking-wider self-center mr-2">
                Citations:
              </span>
              <button
                onClick={() =>
                  setActiveCitationSegment({
                    title: "RFC 791 (IPv4 Spec)",
                    content:
                      "RFC 791 defines the internet protocol standard, laying down guidelines for addressing nodes, assembling header structures, handling fragmentation, and routing packets basic hop limits.",
                  })
                }
                className="bg-surface-container-high hover:border-primary border border-outline-variant hover:text-primary px-3 py-1 rounded font-bold text-[10px] text-on-surface-variant flex items-center gap-1.5 cursor-pointer select-none"
              >
                <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>description</span>
                RFC 791 (IPv4 spec)
              </button>
              <button
                onClick={() =>
                  setActiveCitationSegment({
                    title: "DNS Architecture Doc (v2)",
                    content:
                      "This document describes the high scale naming topology, outlining recursive path lookup strategies from root name systems to authoritative registers, including heavy DNS caching intervals.",
                  })
                }
                className="bg-surface-container-high hover:border-primary border border-outline-variant hover:text-primary px-3 py-1 rounded font-bold text-[10px] text-on-surface-variant flex items-center gap-1.5 cursor-pointer select-none"
              >
                <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>description</span>
                DNS Arch Doc
              </button>
            </div>
          </section>

          {/* Interactive Detailed Breakdown Section */}
          <section className="space-y-4">
            <h3 className="text-lg font-headline font-bold text-on-surface border-b border-outline-variant pb-2">
              Detailed Breakdown
            </h3>

            <div className="space-y-3">
              {(module.detailedBreakdown || []).map((item, idx) => {
                const isOpen = activeAccordionIndex === idx;
                return (
                  <div
                    key={idx}
                    className="bg-surface-container-lowest border border-outline-variant rounded-xl overflow-hidden transition-all shadow-sm"
                  >
                    <button
                      onClick={() => setActiveAccordionIndex(isOpen ? null : idx)}
                      className="w-full flex items-center justify-between p-4 font-headline font-bold text-sm text-left hover:bg-surface-container-low/50 transition-colors select-none"
                    >
                      <div className="flex items-center gap-3">
                        <span
                          className={`material-symbols-outlined text-primary transition-transform ${
                            isOpen ? "rotate-90" : ""
                          }`}
                        >
                          chevron_right
                        </span>
                        <span className="text-on-surface text-base">{item.title}</span>
                      </div>
                      <span className="text-[10px] font-mono font-bold tracking-wider px-2 py-0.5 rounded bg-surface-container text-on-surface-variant uppercase border border-outline-variant/30">
                        {item.tag}
                      </span>
                    </button>

                    {isOpen && (
                      <div className="p-4 pt-2 border-t border-outline-variant/20 bg-surface-container-low/20 space-y-3 animate-fade-in text-sm text-on-surface-variant leading-relaxed">
                        <p>{item.content}</p>
                        {item.bullets && item.bullets.length > 0 && (
                          <ul className="list-disc pl-5 space-y-1.5 text-on-surface/85 font-medium">
                            {item.bullets.map((b, bIdx) => (
                              <li key={bIdx}>{b}</li>
                            ))}
                          </ul>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </section>
        </div>

        {/* Right Side (Sidebar - Span 1) */}
        <div className="space-y-8">
          {/* Your Progress Tracking Side Panel Card */}
          <div className="bg-surface-container-lowest border border-status-completed/20 rounded-xl p-5 relative overflow-hidden shadow-sm">
            <div className="absolute top-0 left-0 right-0 h-1 bg-status-completed" />

            <h3 className="text-lg font-headline font-extrabold text-on-surface mb-3 flex items-center justify-between">
              Your Progress
              <span className="text-status-completed font-bold text-[11px] bg-status-completed/10 px-2 py-0.5 rounded">
                {module.progress}%
              </span>
            </h3>

            {/* Checklist of steps */}
            <div className="space-y-2.5 mb-6">
              {module.tasks.map((task) => (
                <label
                  key={task.id}
                  className="flex items-start gap-2.5 cursor-pointer text-[11px] font-headline font-medium text-on-surface-variant hover:text-on-surface select-none mt-1"
                >
                  <div
                    className={`mt-0.5 w-4 h-4 rounded border flex items-center justify-center shrink-0 transition-colors ${
                      task.completed
                        ? "bg-status-completed border-status-completed text-white"
                        : "border-outline-variant hover:border-primary"
                    }`}
                  >
                    {task.completed && (
                      <span className="material-symbols-outlined" style={{ fontSize: '12px' }}>check</span>
                    )}
                  </div>
                  <input
                    type="checkbox"
                    checked={task.completed}
                    onChange={() => handleToggleTask(task.id)}
                    className="hidden"
                  />
                  <span className={task.completed ? "line-through text-outline/80" : ""}>
                    {task.title}
                  </span>
                </label>
              ))}
            </div>

            {/* Action complete button */}
            <button
              onClick={handleConquerModule}
              disabled={module.progress === 100}
              className={`w-full py-2.5 rounded-lg text-[11px] font-headline font-bold flex items-center justify-center gap-1.5 transition-all outline-none ${
                module.progress === 100
                  ? "bg-surface-container border border-outline-variant text-outline cursor-default opacity-85"
                  : "bg-status-completed/10 hover:bg-status-completed/15 border border-status-completed text-status-completed cursor-pointer hover:-translate-y-0.5"
              }`}
            >
              <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>
                {module.progress === 100 ? "task_alt" : "verified"}
              </span>
              {module.progress === 100 ? "Module Conquered" : "Complete Module"}
            </button>
          </div>

          {/* Connected Concepts Tags Cloud */}
          <div className="bg-surface-container-lowest border border-outline-variant rounded-xl p-5 shadow-sm">
            <h3 className="text-base font-headline font-extrabold text-on-surface mb-3 flex items-center gap-1.5 select-none">
              <span className="material-symbols-outlined text-on-surface-variant" style={{ fontSize: '18px' }}>share</span>
              Connected Concepts
            </h3>
            <div className="flex flex-wrap gap-2">
              {(module.connectedConcepts || []).map((tag, idx) => (
                <button
                  key={idx}
                  onClick={() => handleWordClick(tag)}
                  className="bg-surface-container-low hover:bg-surface-container-high border border-outline-variant/60 px-3 py-1.5 rounded-full text-[11px] font-headline font-bold text-on-surface transition-all hover:scale-105 active:scale-95 text-left select-none"
                >
                  <span className="inline-block w-1.5 h-1.5 rounded-full bg-primary/80 mr-1.5"></span>
                  {tag}
                </button>
              ))}
            </div>
          </div>

          {/* RAG Sources Lists */}
          <div className="bg-surface-container-lowest border border-outline-variant rounded-xl p-5 shadow-sm">
            <h3 className="text-base font-headline font-extrabold text-on-surface mb-3 flex items-center gap-1.5 select-none">
              <span className="material-symbols-outlined text-on-surface-variant" style={{ fontSize: '18px' }}>library_books</span>
              RAG Sources
            </h3>
            <ul className="space-y-3">
              {(module.ragSources || []).map((source, idx) => {
                const icon =
                  source.type === "pdf"
                    ? "picture_as_pdf"
                    : source.type === "link"
                    ? "link"
                    : "article";
                const bg =
                  source.type === "pdf"
                    ? "bg-error-container text-error"
                    : source.type === "link"
                    ? "bg-primary-container/20 text-primary"
                    : "bg-secondary-container text-secondary";

                return (
                  <li key={idx}>
                    <button
                      onClick={() =>
                        setActiveCitationSegment({
                          title: source.title,
                          content: `Retrieved contextual snippet for analysis from source metadata: This file addresses the specific parameters, rulesets, or specifications governing "${module.title}" context systems.`,
                        })
                      }
                      className="w-full text-left flex items-start gap-3 p-2 -mx-2 rounded-lg hover:bg-surface-container transition-all group select-none border-0 bg-transparent cursor-pointer"
                    >
                      <div
                        className={`w-8 h-8 rounded flex items-center justify-center shrink-0 group-hover:scale-105 transition-transform ${bg}`}
                      >
                        <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>{icon}</span>
                      </div>
                      <div>
                        <div className="text-[11px] font-headline font-bold text-on-surface group-hover:text-primary transition-colors truncate max-w-[180px]">
                          {source.title}
                        </div>
                        <div className="font-mono text-[9px] text-outline mt-0.5">{source.details}</div>
                      </div>
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        </div>
      </div>

      {/* Floating Glossary Explainer Drawer UI Overlay */}
      {activeGlossaryTerm && (
        <div className="fixed inset-0 bg-black/40 backdrop-blur-sm flex items-center justify-end z-[100] animate-fade-in">
          <div className="bg-surface-container-lowest border-l border-outline-variant w-full max-w-md h-full p-6 flex flex-col justify-between shadow-2xl animate-slide-left">
            <div className="space-y-6">
              <div className="flex justify-between items-center border-b border-outline-variant pb-3">
                <div className="flex items-center gap-2">
                  <span className="material-symbols-outlined text-primary" style={{ fontSize: '20px' }}>auto_awesome</span>
                  <span className="text-[11px] font-mono font-bold text-outline uppercase tracking-wider">
                    Term Explainer
                  </span>
                </div>
                <button
                  onClick={() => setActiveGlossaryTerm(null)}
                  className="text-on-surface-variant hover:text-on-surface p-1 hover:bg-surface-container rounded-full select-none"
                >
                  <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>close</span>
                </button>
              </div>

              <div className="space-y-2">
                <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-primary-fixed/20 text-primary uppercase border border-primary/10">
                  {activeGlossaryTerm.category}
                </span>
                <h4 className="text-2xl font-headline font-extrabold text-on-surface">
                  {activeGlossaryTerm.term}
                </h4>
                <p className="text-sm text-on-surface-variant leading-relaxed">
                  {activeGlossaryTerm.desc}
                </p>
              </div>

              <div className="bg-surface-container p-4 rounded-lg border border-outline-variant/40 space-y-1 text-[11px]">
                <div className="font-bold text-on-surface flex items-center gap-1">
                  <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>info</span>
                  Educational Guardrails
                </div>
                <div className="text-on-surface-variant">
                  This term is flagged as a key node concept. Click any highlighted node or term in the
                  synthesized insights panel to query dictionary details.
                </div>
              </div>
            </div>

            <button
              onClick={() => setActiveGlossaryTerm(null)}
              className="w-full bg-primary text-on-primary py-2.5 rounded-lg font-bold text-[11px] focus:ring-2 focus:ring-primary/20 select-none border-0"
            >
              Continue Study Session
            </button>
          </div>
        </div>
      )}

      {/* Floating Citation Segment Snippet Drawer UI Overlay */}
      {activeCitationSegment && (
        <div className="fixed inset-0 bg-black/40 backdrop-blur-sm flex items-center justify-end z-[100] animate-fade-in">
          <div className="bg-surface-container-lowest border-l border-outline-variant w-full max-w-md h-full p-6 flex flex-col justify-between shadow-2xl animate-slide-left">
            <div className="space-y-6">
              <div className="flex justify-between items-center border-b border-outline-variant pb-3">
                <div className="flex items-center gap-2">
                  <span className="material-symbols-outlined text-primary" style={{ fontSize: '20px' }}>library_books</span>
                  <span className="text-[11px] font-mono font-bold text-outline uppercase tracking-wider">
                    RAG Chunk Retrieval
                  </span>
                </div>
                <button
                  onClick={() => setActiveCitationSegment(null)}
                  className="text-on-surface-variant hover:text-on-surface p-1 hover:bg-surface-container rounded-full select-none"
                >
                  <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>close</span>
                </button>
              </div>

              <div className="space-y-3">
                <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded bg-error-container text-error uppercase border border-error/10">
                  Retrieved Chunk
                </span>
                <h4 className="text-xl font-headline font-extrabold text-on-surface">
                  {activeCitationSegment.title}
                </h4>
                <div className="bg-[#0f172a] text-[#38edf8] p-4 rounded-lg font-mono text-[11px] leading-relaxed border border-[#1e293b] select-all">
                  &ldquo;{activeCitationSegment.content}&rdquo;
                </div>
              </div>

              <div className="bg-surface-container p-4 rounded-lg border border-outline-variant/40 space-y-1 text-[11px]">
                <div className="font-bold text-on-surface flex items-center gap-1">
                  <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>database</span>
                  Vector Grounding Claim
                </div>
                <div className="text-on-surface-variant">
                  This chunk was fetched from local vector store segments dynamically based on semantic
                  query match.
                </div>
              </div>
            </div>

            <button
              onClick={() => setActiveCitationSegment(null)}
              className="w-full bg-primary text-on-primary py-2.5 rounded-lg font-bold text-[11px] select-none border-0"
            >
              Dismiss retrieved document
            </button>
          </div>
        </div>
      )}

      {/* Write Personal Notes Pad Modal Dialog */}
      {showingNotePad && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-[100] animate-fade-in">
          <div className="bg-surface-container-lowest border border-outline-variant w-full max-w-lg rounded-xl overflow-hidden shadow-2xl animate-scale-up space-y-4 p-6">
            <div className="flex justify-between items-center border-b border-outline-variant pb-3">
              <h3 className="text-lg font-headline font-bold text-on-surface flex items-center gap-2">
                <span className="material-symbols-outlined text-primary" style={{ fontSize: '20px' }}>bookmark</span>
                Study Notes: {module.title}
              </h3>
              <button
                onClick={() => setShowingNotePad(false)}
                className="text-on-surface-variant hover:text-on-surface p-1 hover:bg-surface-container rounded-full select-none"
              >
                <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>close</span>
              </button>
            </div>

            <div className="space-y-1">
              <label className="text-[10px] font-headline font-bold text-on-surface-variant uppercase tracking-wider block">
                My Commentary
              </label>
              <textarea
                id="notes-notepad-textarea"
                rows={6}
                value={noteContent}
                onChange={(e) => setNoteContent(e.target.value)}
                placeholder="Write your notes, code snippets, or key concepts to remember here..."
                className="w-full bg-surface-container-lowest border border-outline-variant rounded-lg p-3 font-headline text-sm text-on-surface focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors"
              />
            </div>

            <div className="flex justify-end gap-3 mt-4 pt-4 border-t border-outline-variant/30">
              <button
                onClick={() => setShowingNotePad(false)}
                className="px-4 py-2 text-[11px] font-headline font-bold hover:bg-surface-container rounded-lg text-on-surface select-none border-0 bg-transparent cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveNotes}
                className="bg-primary hover:bg-primary-container text-on-primary px-5 py-2 rounded-lg font-headline font-bold text-[11px] shadow-sm cursor-pointer select-none border-0"
              >
                Save Notes
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
