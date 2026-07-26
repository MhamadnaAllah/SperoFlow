"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError, authApi } from "@/lib/api/client";

export function useUser() {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const currentUser = await authApi.me();
      setUser(currentUser);
      return currentUser;
    } catch (error) {
      if (!(error instanceof ApiError) || error.status === 401 || error.status === 403) {
        setUser(null);
        return null;
      }
      setUser(null);
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      const currentUser = await refresh();
      if (!mounted) return;
      setUser(currentUser);
    };
    const onAuthChange = () => {
      void refresh();
    };

    void load();
    window.addEventListener("speroflow-auth-change", onAuthChange);
    return () => {
      mounted = false;
      window.removeEventListener("speroflow-auth-change", onAuthChange);
    };
  }, [refresh]);

  return { user, loading, refresh };
}