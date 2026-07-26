"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";

import { ApiError, authApi } from "@/lib/api/client";

const loginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Enter your password."),
  rememberMe: z.boolean().default(false),
});

const signupSchema = z
  .object({
    displayName: z.string().trim().min(2, "Enter at least two characters.").max(160),
    email: z.string().email("Enter a valid email address."),
    password: z
      .string()
      .min(12, "Use at least 12 characters.")
      .regex(/[A-Z]/, "Include an uppercase letter.")
      .regex(/[a-z]/, "Include a lowercase letter.")
      .regex(/[0-9]/, "Include a number.")
      .regex(/[^A-Za-z0-9]/, "Include a symbol."),
    confirmPassword: z.string(),
    terms: z.boolean().refine((value) => value === true, { message: "Accept the terms to continue." }),
  })
  .refine((data) => data.password === data.confirmPassword, {
    path: ["confirmPassword"],
    message: "Passwords do not match.",
  });

function Field({ children, error, icon, id, label }) {
  return (
    <label className="block" htmlFor={id}>
      <span className="mb-1.5 block text-xs font-bold text-on-surface-variant">{label}</span>
      <span className="relative block">
        <span className="material-symbols-outlined pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant" style={{ fontSize: "18px" }}>
          {icon}
        </span>
        {children}
      </span>
      {error && <span className="mt-1.5 block text-xs font-medium text-error">{error}</span>}
    </label>
  );
}

const inputClassName =
  "w-full rounded-lg border border-outline-variant/50 bg-surface px-3 py-2.5 pl-10 text-sm text-on-surface placeholder:text-on-surface-variant/60 focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20";

function oidcAuthorizationReturnPath(value) {
  if (!value || !value.startsWith("/connect/authorize")) {
    return null;
  }

  return value;
}

export default function AuthFlow({ initialMode = "login" }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const signupMode = initialMode === "signup";
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const loginForm = useForm({ resolver: zodResolver(loginSchema), defaultValues: { email: "", password: "", rememberMe: false } });
  const signupForm = useForm({ resolver: zodResolver(signupSchema), defaultValues: { displayName: "", email: "", password: "", confirmPassword: "", terms: false } });
  const form = signupMode ? signupForm : loginForm;

  const onSubmit = async (values) => {
    setError(null);
    setNotice(null);
    try {
      if (signupMode) {
        const result = await authApi.register({
          email: values.email,
          password: values.password,
          displayName: values.displayName,
        });
        if (result?.emailConfirmationRequired) {
          setNotice("Check your email to confirm your account before signing in.");
        } else {
          router.replace("/login");
        }
        return;
      }

      await authApi.login({ email: values.email, password: values.password, rememberMe: values.rememberMe });
      const returnPath = oidcAuthorizationReturnPath(searchParams.get("returnUrl"));
      if (returnPath) {
        window.location.assign(returnPath);
        return;
      }

      router.replace("/calendar");
      router.refresh();
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : "Something went wrong. Please try again.");
    }
  };

  return (
    <main className="min-h-screen bg-surface px-4 py-8 sm:px-6 sm:py-12">
      <div className="mx-auto grid min-h-[38rem] max-w-5xl overflow-hidden rounded-xl border border-outline-variant/30 bg-surface-container-lowest shadow-xl lg:grid-cols-[1.05fr_0.95fr]">
        <section className="hidden min-h-full bg-primary p-10 text-on-primary lg:flex lg:flex-col lg:justify-between">
          <div className="flex items-center gap-3 text-lg font-extrabold">
            <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-white/15">
              <span className="material-symbols-outlined" style={{ fontSize: "20px", fontVariationSettings: "'FILL' 1" }}>calendar_month</span>
            </span>
            SperoFlow
          </div>
          <div>
            <p className="text-sm font-semibold text-white/70">Personal operating system</p>
            <h1 className="mt-3 max-w-md text-4xl font-bold leading-tight">Make room for what matters.</h1>
            <p className="mt-4 max-w-sm text-sm leading-relaxed text-white/75">Projects, tasks, habits, and thoughtful AI support in one calm workspace.</p>
          </div>
          <div className="grid grid-cols-3 gap-3 text-center text-xs font-semibold text-white/80">
            <span className="rounded-lg border border-white/15 bg-white/10 px-3 py-3">Projects</span>
            <span className="rounded-lg border border-white/15 bg-white/10 px-3 py-3">Balance</span>
            <span className="rounded-lg border border-white/15 bg-white/10 px-3 py-3">Focus</span>
          </div>
        </section>

        <section className="flex items-center p-6 sm:p-10">
          <div className="mx-auto w-full max-w-sm">
            <Link className="mb-10 flex items-center gap-2 text-base font-extrabold text-primary lg:hidden" href="/">
              <span className="material-symbols-outlined" style={{ fontSize: "20px", fontVariationSettings: "'FILL' 1" }}>calendar_month</span>
              SperoFlow
            </Link>
            <p className="text-xs font-bold uppercase text-primary">{signupMode ? "Create account" : "Welcome back"}</p>
            <h2 className="mt-2 text-3xl font-bold text-on-surface">{signupMode ? "Start your workspace" : "Sign in"}</h2>
            <p className="mt-2 text-sm text-on-surface-variant">
              {signupMode ? "Your account keeps your workspace private and in sync." : "Continue where you left off."}
            </p>

            {error && <p className="mt-5 rounded-lg border border-error/20 bg-error/10 px-3 py-2.5 text-sm font-medium text-error">{error}</p>}
            {notice && <p className="mt-5 rounded-lg border border-secondary/20 bg-secondary/10 px-3 py-2.5 text-sm font-medium text-secondary">{notice}</p>}

            <form className="mt-7 space-y-4" onSubmit={form.handleSubmit(onSubmit)}>
              {signupMode && (
                <Field error={form.formState.errors.displayName?.message} icon="person" id="displayName" label="Name">
                  <input className={inputClassName} id="displayName" placeholder="Your name" {...form.register("displayName")} />
                </Field>
              )}
              <Field error={form.formState.errors.email?.message} icon="mail" id="email" label="Email">
                <input autoComplete="email" className={inputClassName} id="email" placeholder="you@example.com" type="email" {...form.register("email")} />
              </Field>
              <Field error={form.formState.errors.password?.message} icon="lock" id="password" label="Password">
                <input autoComplete={signupMode ? "new-password" : "current-password"} className={inputClassName} id="password" type="password" {...form.register("password")} />
              </Field>
              {signupMode && (
                <>
                  <Field error={form.formState.errors.confirmPassword?.message} icon="lock_reset" id="confirmPassword" label="Confirm password">
                    <input autoComplete="new-password" className={inputClassName} id="confirmPassword" type="password" {...form.register("confirmPassword")} />
                  </Field>
                  <label className="flex items-start gap-2 text-xs text-on-surface-variant">
                    <input className="mt-0.5 h-4 w-4 accent-primary" type="checkbox" {...form.register("terms")} />
                    <span>I agree to the terms of use and privacy policy.</span>
                  </label>
                  {form.formState.errors.terms && <p className="text-xs font-medium text-error">{form.formState.errors.terms.message}</p>}
                </>
              )}
              {!signupMode && (
                <label className="flex items-center gap-2 text-xs text-on-surface-variant">
                  <input className="h-4 w-4 accent-primary" type="checkbox" {...form.register("rememberMe")} />
                  <span>Keep me signed in</span>
                </label>
              )}
              <button className="flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-4 py-3 text-sm font-bold text-on-primary shadow-sm transition-colors hover:bg-primary-dim disabled:cursor-not-allowed disabled:opacity-50" disabled={form.formState.isSubmitting} type="submit">
                {form.formState.isSubmitting ? "Please wait" : signupMode ? "Create account" : "Sign in"}
                {!form.formState.isSubmitting && <span className="material-symbols-outlined" style={{ fontSize: "17px" }}>arrow_forward</span>}
              </button>
            </form>

            <p className="mt-7 text-center text-sm text-on-surface-variant">
              {signupMode ? "Already have an account?" : "Need an account?"}{" "}
              <Link className="font-bold text-primary hover:text-primary-dim" href={signupMode ? "/login" : "/signup"}>
                {signupMode ? "Sign in" : "Create one"}
              </Link>
            </p>
          </div>
        </section>
      </div>
    </main>
  );
}