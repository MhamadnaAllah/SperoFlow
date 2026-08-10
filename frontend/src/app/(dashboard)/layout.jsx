import { redirect } from "next/navigation";

import BrainChat from "@/features/AgenticHub/components/BrainChatLoader";
import DashboardShell from "@/components/layout/DashboardShell";
import DndProvider from "@/components/providers/DndProvider";
import { getServerCurrentUser } from "@/lib/api/server";

export const dynamic = "force-dynamic";

export default async function DashboardLayout({ children }) {
  const user = await getServerCurrentUser();
  if (!user) {
    redirect("/login");
  }

  return (
    <DndProvider>
      <DashboardShell user={user}>{children}</DashboardShell>
      <BrainChat />
    </DndProvider>
  );
}