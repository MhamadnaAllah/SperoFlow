"use client";

/**
 * Journaling feature — default view.
 *
 * This acts as the entry-point shell that composes the
 * AI Assistant (write + recall panes) into the page layout.
 * Any future sub-views (e.g., a calendar heatmap) can be
 * added here without touching the page route.
 */
import AIAssistant from './AIAssistant';

export default function JournalingView() {
  return (
    <div className="h-full bg-surface overflow-y-auto">
      <AIAssistant />
    </div>
  );
}
