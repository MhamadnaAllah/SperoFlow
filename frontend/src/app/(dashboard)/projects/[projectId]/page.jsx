import { ProjectWorkspace } from "@/features/Projects";

export default async function ProjectWorkspacePage({ params }) {
  const { projectId } = await params;
  return <ProjectWorkspace projectId={projectId} />;
}
