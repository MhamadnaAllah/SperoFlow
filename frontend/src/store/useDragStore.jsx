"use client";

import { createContext, useContext, useState } from "react";

/**
 * Global drag-and-drop store for SperoFlow.
 * Provides Context to share currently dragged item metadata across components,
 * used by DndProvider for DragOverlay and event routing.
 */

const DragStoreContext = createContext(null);

export function DragStoreProvider({ children }) {
  const [activeItem, setActiveItem] = useState(null);

  function startDrag(item) {
    setActiveItem(item);
  }

  function endDrag() {
    setActiveItem(null);
  }

  return (
    <DragStoreContext.Provider value={{ activeItem, startDrag, endDrag }}>
      {children}
    </DragStoreContext.Provider>
  );
}

export function useDragStore() {
  const ctx = useContext(DragStoreContext);
  if (!ctx) throw new Error("useDragStore must be used inside <DragStoreProvider>");
  return ctx;
}
