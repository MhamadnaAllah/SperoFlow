import { Suspense } from "react";
import AuthFlow from "@/components/auth/AuthFlow";

export const metadata = {
  title: "Sign In · SperoFlow",
  description: "Sign in to your SperoFlow AI life dashboard.",
};

function LoginFallback() {
  return (
    <div className="min-h-screen w-full flex items-center justify-center bg-surface">
      <span className="w-7 h-7 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<LoginFallback />}>
      <AuthFlow initialMode="login" />
    </Suspense>
  );
}
