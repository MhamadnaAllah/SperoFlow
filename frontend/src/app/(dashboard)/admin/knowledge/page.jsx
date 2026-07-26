import { redirect } from "next/navigation";

import { getServerCurrentUser } from "@/lib/api/server";

export const dynamic = "force-dynamic";

export default async function KnowledgeDatasetsAdminPage() {
  const user = await getServerCurrentUser();
  if (!user) redirect("/login");
  if (!Array.isArray(user.roles) || !user.roles.some((role) => String(role).toLowerCase() === "admin")) {
    redirect("/settings");
  }

  redirect(process.env.KNOWLEDGE_PORTAL_URL || "https://knowledge.example.com");
}
