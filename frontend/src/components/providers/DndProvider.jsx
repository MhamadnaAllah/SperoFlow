"use client";

import {
  DndContext,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
  DragOverlay,
  pointerWithin,
} from "@dnd-kit/core";
import { sortableKeyboardCoordinates } from "@dnd-kit/sortable";
import { DragStoreProvider, useDragStore } from "@/store/useDragStore";

function DndInner({ children }) {
  const { activeItem, startDrag, endDrag } = useDragStore();

  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: { distance: 8 },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const handleDragStart = (event) => {
    const { active } = event;
    startDrag({
      id: active.id,
      type: active.data.current?.type ?? "unknown",
      data: active.data.current ?? {},
    });
  };

  const handleDragEnd = (event) => {
    const { over } = event;
    if (over) {
      const overType = over.data.current?.type;

      if (overType === "calendar-day" && typeof window.__calendarDragEnd === "function") {
        window.__calendarDragEnd(event);
      } else if (
        (overType === "quadrant" || overType === "matrix-task") &&
        typeof window.__matrixDragEnd === "function"
      ) {
        window.__matrixDragEnd(event);
      } else if (
        (overType === "task-column" || overType === "task-item") &&
        typeof window.__tasksDragEnd === "function"
      ) {
        window.__tasksDragEnd(event);
      }
    }
    endDrag();
  };

  const handleDragCancel = () => endDrag();

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={pointerWithin}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
      {children}

      <DragOverlay dropAnimation={{ duration: 200, easing: "ease" }}>
        {activeItem ? (
          <div
            className="bg-white rounded-2xl px-4 py-3 shadow-2xl border border-primary/20 text-sm font-semibold text-slate-700 pointer-events-none"
            style={{ maxWidth: "240px", opacity: 0.95, transform: "rotate(2deg)" }}
          >
            <div className="flex items-center gap-2">
              <span className="material-symbols-outlined text-primary" style={{ fontSize: "16px" }}>
                drag_indicator
              </span>
              <span className="truncate">{activeItem.data?.title ?? activeItem.id}</span>
            </div>
            {activeItem.data?.tag && (
              <span
                className="mt-1 block text-[9px] font-bold uppercase tracking-widest text-primary"
              >
                {activeItem.data.tag}
              </span>
            )}
          </div>
        ) : null}
      </DragOverlay>
    </DndContext>
  );
}

export default function DndProvider({ children }) {
  return (
    <DragStoreProvider>
      <DndInner>{children}</DndInner>
    </DragStoreProvider>
  );
}
