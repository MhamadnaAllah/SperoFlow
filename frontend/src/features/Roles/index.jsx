"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import { ApiError, aiApi, aiProposalsApi, rolesApi } from "@/lib/api/client";

const LIFE_AREAS = ["work", "family", "physical", "spiritual", "social", "learning", "personal"];
const ROLE_COLORS = ["#0053dc", "#dc2626", "#047857", "#a16207", "#7c3aed", "#c2410c"];
const ROLE_ICONS = ["person", "work", "family_restroom", "favorite", "rocket_launch", "school", "volunteer_activism"];

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function RoleModal({ onClose, onSave, role, saving }) {
  const [form, setForm] = useState(() => ({
    name: role?.name || "",
    category: role?.category || "external",
    defaultLifeArea: role?.defaultLifeArea || "personal",
    color: role?.color || ROLE_COLORS[0],
    icon: role?.icon || "person",
  }));
  const update = (key, value) => setForm((current) => ({ ...current, [key]: value }));
  const systemRole = Boolean(role?.isSystemRole);

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/35 p-4" onMouseDown={onClose}>
      <form
        aria-modal="true"
        className="w-full max-w-lg rounded-lg border border-outline-variant/30 bg-white shadow-2xl"
        onMouseDown={(event) => event.stopPropagation()}
        onSubmit={(event) => {
          event.preventDefault();
          if (!form.name.trim()) return;
          onSave({ ...form, name: form.name.trim() });
        }}
      >
        <header className="flex items-center justify-between border-b border-outline-variant/20 px-5 py-4">
          <h2 className="text-lg font-bold text-on-surface">{role ? "Edit role" : "New role"}</h2>
          <button aria-label="Close role editor" className="flex h-8 w-8 items-center justify-center rounded-lg hover:bg-surface-container" onClick={onClose} title="Close" type="button"><span className="material-symbols-outlined">close</span></button>
        </header>
        <div className="space-y-4 p-5">
          <label className="block text-sm font-semibold text-on-surface">Role name<input autoFocus className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" maxLength="160" onChange={(event) => update("name", event.target.value)} value={form.name} /></label>
          <div>
            <p className="text-sm font-semibold text-on-surface">Role type</p>
            <div className="mt-1.5 inline-flex rounded-lg border border-outline-variant/40 bg-surface p-1" role="radiogroup" aria-label="Role type">
              {["internal", "external"].map((category) => <button aria-checked={form.category === category} className={`rounded-md px-3 py-1.5 text-xs font-bold ${form.category === category ? "bg-primary text-on-primary" : "text-on-surface-variant hover:bg-white"}`} disabled={systemRole && category !== "internal"} key={category} onClick={() => update("category", category)} role="radio" type="button">{titleCase(category)}</button>)}
            </div>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="block text-sm font-semibold text-on-surface">Default life area<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("defaultLifeArea", event.target.value)} value={form.defaultLifeArea}>{LIFE_AREAS.map((area) => <option key={area} value={area}>{titleCase(area)}</option>)}</select></label>
            <label className="block text-sm font-semibold text-on-surface">Icon<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("icon", event.target.value)} value={form.icon}>{ROLE_ICONS.map((icon) => <option key={icon} value={icon}>{titleCase(icon)}</option>)}</select></label>
          </div>
          <div>
            <p className="text-sm font-semibold text-on-surface">Color</p>
            <div className="mt-2 flex flex-wrap gap-2" role="radiogroup" aria-label="Role color">{ROLE_COLORS.map((color) => <button aria-checked={form.color === color} aria-label={`Use ${color}`} className={`h-8 w-8 rounded-full border-2 ${form.color === color ? "border-on-surface" : "border-transparent"}`} key={color} onClick={() => update("color", color)} role="radio" style={{ background: color }} title={`Use ${color}`} type="button" />)}</div>
          </div>
        </div>
        <footer className="flex justify-end gap-3 border-t border-outline-variant/20 px-5 py-4"><button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onClose} type="button">Cancel</button><button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.name.trim()} type="submit">{saving ? "Saving" : role ? "Save" : "Create"}</button></footer>
      </form>
    </div>
  );
}

function RoleRow({ onArchive, onEdit, onRestore, role, saving }) {
  return (
    <article className="flex items-center gap-3 border-b border-outline-variant/20 py-3 last:border-b-0">
      <span className="material-symbols-outlined flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-surface-container" style={{ color: role.color, fontVariationSettings: "'FILL' 1" }}>{role.icon}</span>
      <div className="min-w-0 flex-1"><h3 className="truncate text-sm font-bold text-on-surface">{role.name}</h3><p className="mt-0.5 text-xs text-on-surface-variant">{titleCase(role.defaultLifeArea)} {role.isSystemRole ? "core role" : `${titleCase(role.category)} role`}</p></div>
      <div className="flex items-center gap-1">
        <button aria-label={`Edit ${role.name}`} className="flex h-8 w-8 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-on-surface" disabled={saving} onClick={() => onEdit(role)} title={`Edit ${role.name}`} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>edit</span></button>
        {role.isArchived ? <button aria-label={`Restore ${role.name}`} className="flex h-8 w-8 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-on-surface" disabled={saving} onClick={() => onRestore(role)} title={`Restore ${role.name}`} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>restore</span></button> : !role.isSystemRole && <button aria-label={`Archive ${role.name}`} className="flex h-8 w-8 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-error" disabled={saving} onClick={() => onArchive(role)} title={`Archive ${role.name}`} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>archive</span></button>}
      </div>
    </article>
  );
}

function ProposalRow({ onApprove, onCancel, proposal, saving }) {
  return (
    <article className="border-b border-outline-variant/20 py-4 last:border-b-0 sm:flex sm:items-start sm:justify-between sm:gap-5">
      <div className="min-w-0"><div className="flex items-center gap-2"><span className="material-symbols-outlined text-primary" style={{ fontSize: "18px" }}>auto_awesome</span><h3 className="truncate text-sm font-bold text-on-surface">{proposal.title}</h3></div><p className="mt-2 max-w-2xl text-sm leading-relaxed text-on-surface-variant">{proposal.description}</p><p className="mt-2 text-[11px] font-semibold uppercase text-on-surface-variant">{titleCase(proposal.source)} suggestion</p></div>
      <div className="mt-3 flex flex-shrink-0 gap-2 sm:mt-0"><button className="rounded-lg border border-outline-variant/40 px-3 py-2 text-xs font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={saving} onClick={() => onCancel(proposal)} type="button">Cancel</button><button className="rounded-lg bg-primary px-3 py-2 text-xs font-bold text-on-primary disabled:opacity-50" disabled={saving} onClick={() => onApprove(proposal)} type="button">Approve</button></div>
    </article>
  );
}

function RoleDiscoveryCandidateRow({ candidate, onApprove, onCancel, saving }) {
  const { proposal, evidence } = candidate;
  return <article className="border-b border-outline-variant/20 py-4 last:border-b-0 sm:flex sm:items-start sm:justify-between sm:gap-5">
    <div className="min-w-0"><div className="flex items-center gap-2"><span className="material-symbols-outlined text-primary" style={{ fontSize: "18px" }}>auto_awesome</span><h3 className="truncate text-sm font-bold text-on-surface">{proposal.title}</h3></div><p className="mt-2 max-w-2xl text-sm leading-relaxed text-on-surface-variant">{proposal.description}</p><ul className="mt-3 space-y-1.5">{evidence.map((item) => <li className="flex gap-2 text-xs leading-relaxed text-on-surface-variant" key={item}><span className="material-symbols-outlined mt-0.5 text-primary" style={{ fontSize: "14px" }}>subdirectory_arrow_right</span><span>{item}</span></li>)}</ul></div>
    <div className="mt-3 flex flex-shrink-0 gap-2 sm:mt-0"><button className="rounded-lg border border-outline-variant/40 px-3 py-2 text-xs font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={saving} onClick={() => onCancel(proposal)} type="button">Cancel</button><button className="rounded-lg bg-primary px-3 py-2 text-xs font-bold text-on-primary disabled:opacity-50" disabled={saving} onClick={() => onApprove(proposal)} type="button">Approve</button></div>
  </article>;
}
export default function RolesView() {
  const [roles, setRoles] = useState([]);
  const [proposals, setProposals] = useState([]);
  const [roleCandidates, setRoleCandidates] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [editorRole, setEditorRole] = useState(undefined);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      await rolesApi.bootstrap();
      const [nextRoles, nextProposals, nextRoleCandidates] = await Promise.all([
        rolesApi.list({ includeArchived: true }),
        aiProposalsApi.list({ state: "pending" }),
        rolesApi.listDiscoveryCandidates(),
      ]);
      setRoles(nextRoles);
      setProposals(nextProposals.filter((proposal) => !["role-discovery", "eisenhower", "graphrag-roadmap"].includes(proposal.source)));
      setRoleCandidates(nextRoleCandidates);
      setNotice(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load roles and suggestions.") });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const activeRoles = useMemo(() => roles.filter((role) => !role.isArchived).sort((left, right) => left.category.localeCompare(right.category) || left.sortOrder - right.sortOrder || left.name.localeCompare(right.name)), [roles]);
  const archivedRoles = useMemo(() => roles.filter((role) => role.isArchived).sort((left, right) => left.name.localeCompare(right.name)), [roles]);
  const saveRole = async (input) => {
    setSaving(true);
    try {
      if (editorRole?.id) {
        const saved = await rolesApi.update(editorRole.id, { ...input, sortOrder: editorRole.sortOrder, concurrencyToken: editorRole.concurrencyToken });
        setRoles((current) => current.map((role) => role.id === saved.id ? saved : role));
      } else {
        const saved = await rolesApi.create(input);
        setRoles((current) => [...current, saved]);
      }
      setEditorRole(undefined);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to save the role.") });
    } finally {
      setSaving(false);
    }
  };
  const archiveRole = async (role) => {
    setSaving(true);
    try {
      const saved = await rolesApi.archive(role.id, role.concurrencyToken);
      setRoles((current) => current.map((item) => item.id === saved.id ? saved : item));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to archive the role.") });
    } finally { setSaving(false); }
  };
  const restoreRole = async (role) => {
    setSaving(true);
    try {
      const saved = await rolesApi.restore(role.id, role.concurrencyToken);
      setRoles((current) => current.map((item) => item.id === saved.id ? saved : item));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to restore the role.") });
    } finally { setSaving(false); }
  };
  const approveProposal = async (proposal) => {
    setSaving(true);
    try {
      const resolved = await aiProposalsApi.approve(proposal.id, proposal.concurrencyToken);
      setProposals((current) => current.filter((item) => item.id !== proposal.id));
      setRoleCandidates((current) => current.filter((item) => item.proposal.id !== proposal.id));
      if (resolved.kind === "createLifeRole" && resolved.state === "approved") {
        setRoles(await rolesApi.list({ includeArchived: true }));
      }
      setNotice({ type: "success", message: "The approved change is now in your workspace." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to approve the suggestion.") });
      await load();
    } finally { setSaving(false); }
  };
  const evaluateBalance = async () => {
    setSaving(true);
    try {
      const result = await aiApi.balance();
      await load();
      setNotice({ type: "success", message: result.requiresConfirmation ? "A balance suggestion is ready for your decision." : result.insight || "Your balance view is up to date." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to evaluate balance right now.") });
    } finally { setSaving(false); }
  };

  const discoverRoles = async () => {
    setSaving(true);
    try {
      const result = await rolesApi.discover();
      setRoleCandidates(await rolesApi.listDiscoveryCandidates());
      setNotice({ type: "success", message: result.candidates.length > 0 ? "Role candidates are ready for your review." : result.evidenceCount > 0 ? "No new role candidates were found in the current workspace evidence." : "Add a little more workspace activity before role discovery can evaluate a pattern." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to discover roles right now.") });
    } finally { setSaving(false); }
  };
  const cancelProposal = async (proposal) => {
    setSaving(true);
    try {
      await aiProposalsApi.cancel(proposal.id, proposal.concurrencyToken);
      setProposals((current) => current.filter((item) => item.id !== proposal.id));
      setRoleCandidates((current) => current.filter((item) => item.proposal.id !== proposal.id));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to cancel the suggestion.") });
      await load();
    } finally { setSaving(false); }
  };

  return <div className="mx-auto w-full max-w-[72rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8"><header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between"><div><p className="text-xs font-bold uppercase text-secondary">Direction</p><h1 className="mt-1 text-3xl font-bold text-on-surface">Life roles</h1><p className="mt-2 max-w-2xl text-sm text-on-surface-variant">Keep the responsibilities and dimensions that matter visible. Suggestions stay pending until you decide.</p></div><div className="flex flex-wrap gap-2"><button className="flex items-center justify-center gap-2 rounded-lg border border-outline-variant/40 px-4 py-2.5 text-sm font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={saving} onClick={evaluateBalance} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>balance</span>Check balance</button><button className="flex items-center justify-center gap-2 rounded-lg border border-outline-variant/40 px-4 py-2.5 text-sm font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={saving} onClick={discoverRoles} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>auto_awesome</span>Discover roles</button><button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setEditorRole(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>New role</button></div></header>{notice && <div className={`mt-4 flex items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm font-medium ${notice.type === "error" ? "border-error/20 bg-error/10 text-error" : "border-secondary/20 bg-secondary/10 text-secondary"}`}><span>{notice.message}</span><button aria-label="Dismiss notice" onClick={() => setNotice(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span></button></div>}<main className="mt-6 space-y-10"><section id="suggestions"><div className="flex items-center justify-between"><div><h2 className="text-lg font-bold text-on-surface">Pending suggestions</h2><p className="mt-1 text-sm text-on-surface-variant">Nothing changes until you approve it.</p></div><span className="rounded-md bg-surface-container px-2 py-1 text-xs font-bold text-on-surface-variant">{proposals.length}</span></div><div className="mt-3 border-t border-outline-variant/20">{loading ? <div className="h-24 animate-pulse border-b border-outline-variant/20 bg-surface-container-low" /> : proposals.length > 0 ? proposals.map((proposal) => <ProposalRow key={proposal.id} onApprove={approveProposal} onCancel={cancelProposal} proposal={proposal} saving={saving} />) : <p className="py-8 text-sm text-on-surface-variant">No suggestions are waiting for a decision.</p>}</div></section><section id="role-candidates"><div className="flex items-center justify-between"><div><h2 className="text-lg font-bold text-on-surface">Role candidates</h2><p className="mt-1 text-sm text-on-surface-variant">Evidence stays private to your account. Add a role only when it fits your life.</p></div><span className="rounded-md bg-surface-container px-2 py-1 text-xs font-bold text-on-surface-variant">{roleCandidates.length}</span></div><div className="mt-3 border-t border-outline-variant/20">{loading ? <div className="h-24 animate-pulse border-b border-outline-variant/20 bg-surface-container-low" /> : roleCandidates.length > 0 ? roleCandidates.map((candidate) => <RoleDiscoveryCandidateRow candidate={candidate} key={candidate.proposal.id} onApprove={approveProposal} onCancel={cancelProposal} saving={saving} />) : <p className="py-8 text-sm text-on-surface-variant">No role candidates are waiting for a decision.</p>}</div></section><section><div><h2 className="text-lg font-bold text-on-surface">Active roles</h2><p className="mt-1 text-sm text-on-surface-variant">Internal dimensions and the roles you choose to carry.</p></div><div className="mt-3 border-t border-outline-variant/20">{loading ? <div className="h-48 animate-pulse bg-surface-container-low" /> : activeRoles.map((role) => <RoleRow key={role.id} onArchive={archiveRole} onEdit={setEditorRole} onRestore={restoreRole} role={role} saving={saving} />)}</div></section>{archivedRoles.length > 0 && <section><h2 className="text-base font-bold text-on-surface">Archived roles</h2><div className="mt-3 border-t border-outline-variant/20">{archivedRoles.map((role) => <RoleRow key={role.id} onArchive={archiveRole} onEdit={setEditorRole} onRestore={restoreRole} role={role} saving={saving} />)}</div></section>}</main>{editorRole !== undefined && <RoleModal onClose={() => !saving && setEditorRole(undefined)} onSave={saveRole} role={editorRole} saving={saving} />}</div>;
}