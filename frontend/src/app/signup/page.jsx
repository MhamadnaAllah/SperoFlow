import { Suspense } from "react";
import AuthFlow from "@/components/auth/AuthFlow";

export const metadata = {
  title: "Create Account · SperoFlow",
  description: "Join SperoFlow and start building the life you've been planning.",
};

function SignupFallback() {
  return (
    <div className="min-h-screen w-full flex items-center justify-center bg-surface">
      <span className="w-7 h-7 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
    </div>
  );
}

export default function SignupPage() {
  return (
    <Suspense fallback={<SignupFallback />}>
      <AuthFlow initialMode="signup" />
    </Suspense>
  );
}
