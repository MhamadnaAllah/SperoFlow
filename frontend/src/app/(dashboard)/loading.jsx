export default function DashboardLoading() {
  return (
    <div className="flex items-center justify-center h-full w-full">
      <div className="flex flex-col items-center gap-6 animate-pulse">
        {/* Skeleton logo */}
        <div className="w-12 h-12 rounded-xl bg-primary/10" />
        {/* Skeleton text lines */}
        <div className="space-y-3 w-64">
          <div className="h-3 bg-surface-container-high rounded-full w-full" />
          <div className="h-3 bg-surface-container-high rounded-full w-3/4" />
          <div className="h-3 bg-surface-container-high rounded-full w-1/2" />
        </div>
        {/* Skeleton cards */}
        <div className="grid grid-cols-2 gap-4 w-80 mt-4">
          <div className="h-24 bg-surface-container-high rounded-2xl" />
          <div className="h-24 bg-surface-container-high rounded-2xl" />
          <div className="h-24 bg-surface-container-high rounded-2xl" />
          <div className="h-24 bg-surface-container-high rounded-2xl" />
        </div>
      </div>
    </div>
  );
}
