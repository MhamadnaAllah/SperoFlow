"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { PortalApiError, portalApi, validateSourceFile } from "@/lib/api";

function formatDate(value) {
  if (!value) return "-";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

function words(value) {
  return String(value || "").replace(/([a-z])([A-Z])/g, "$1 $2");
}

function stateClass(value) {
  return "state state-" + String(value || "").toLowerCase();
}

function progress(dataset) {
  if (!dataset?.sourceFileCount) return 0;
  return Math.round((dataset.completedSourceFileCount / dataset.sourceFileCount) * 100);
}

function DatasetTable({ datasets, selectedId, onSelect, admin }) {
  if (!datasets.length) return <div className="empty-state">No datasets match this view.</div>;

  return (
    <div className="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Dataset</th>
            {admin && <th>Owner</th>}
            <th>Visibility</th>
            <th>Sources</th>
            <th>Release</th>
            <th>Updated</th>
          </tr>
        </thead>
        <tbody>
          {datasets.map((dataset) => (
            <tr key={dataset.id} className={selectedId === dataset.id ? "is-selected" : ""} onClick={() => onSelect(dataset)}>
              <td><strong>{dataset.name}</strong><span>{dataset.description || "No description"}</span></td>
              {admin && <td className="mono">{dataset.ownerSubject}</td>}
              <td><span className={stateClass(dataset.visibility)}>{words(dataset.visibility)}</span></td>
              <td>
                <div className="progress-cell">
                  <span>{dataset.completedSourceFileCount}/{dataset.sourceFileCount}</span>
                  <i><b style={{ width: progress(dataset) + "%" }} /></i>
                </div>
              </td>
              <td>{dataset.publishedReleaseId ? "Published" : "Draft"}</td>
              <td>{formatDate(dataset.updatedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DetailPanel({ dataset, sources, jobs, releases, isAdmin, onRefresh, onMutate }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [owner, setOwner] = useState("");
  const [selectedFile, setSelectedFile] = useState(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState(null);

  useEffect(() => {
    setName(dataset?.name || "");
    setDescription(dataset?.description || "");
    setOwner(dataset?.ownerSubject || "");
    setSelectedFile(null);
    setNotice(null);
  }, [dataset?.id, dataset?.name, dataset?.description, dataset?.ownerSubject]);

  if (!dataset) {
    return <aside className="detail-panel empty-panel"><p>Select a dataset to inspect sources, jobs, and releases.</p></aside>;
  }

  async function run(action, message = "Saved.") {
    setBusy(true);
    setNotice(null);
    try {
      await action();
      await onMutate();
      setNotice({ kind: "success", text: message });
    } catch (error) {
      setNotice({ kind: "error", text: error.message || "The change could not be completed." });
    } finally {
      setBusy(false);
    }
  }

  return (
    <aside className="detail-panel">
      <div className="detail-heading">
        <div><span className="eyebrow">Dataset</span><h2>{dataset.name}</h2></div>
        <button className="icon-button light" type="button" title="Refresh dataset" onClick={onRefresh} disabled={busy}>Refresh</button>
      </div>
      {notice && <p className={"notice " + notice.kind}>{notice.text}</p>}

      <section className="detail-section">
        <div className="section-label">Details</div>
        <label>Name<input value={name} maxLength="240" onChange={(event) => setName(event.target.value)} /></label>
        <label>Description<textarea value={description} maxLength="8000" rows="3" onChange={(event) => setDescription(event.target.value)} /></label>
        <button className="primary-button" type="button" disabled={busy || !name.trim()} onClick={() => run(() => portalApi.updateDataset(dataset.id, { name, description: description || null, concurrencyToken: dataset.concurrencyToken }))}>Save details</button>
      </section>

      <section className="detail-section">
        <div className="section-label">Source files</div>
        <label className="file-picker">
          <span>{selectedFile ? selectedFile.name : "Choose source"}</span>
          <input type="file" accept=".csv,.json,.md,.txt,.docx,.pdf" onChange={(event) => {
            const file = event.target.files?.[0] || null;
            setSelectedFile(file);
            const validation = validateSourceFile(file);
            setNotice(validation ? { kind: "error", text: validation } : null);
          }} />
        </label>
        <button className="secondary-button" type="button" disabled={busy || !selectedFile} onClick={() => run(() => portalApi.uploadSource(dataset.id, selectedFile), "Source verified and queued.")}>Verify and queue source</button>
        <div className="activity-list">
          {sources.length ? sources.map((source) => (
            <div className="activity-row" key={source.id}>
              <div><strong>{source.fileName}</strong><span>{source.contentType} | {Math.ceil(source.expectedSizeBytes / 1024)} KB</span></div>
              <span className={stateClass(source.state)}>{words(source.state)}</span>
            </div>
          )) : <p className="muted">No source files.</p>}
        </div>
      </section>

      <section className="detail-section">
        <div className="section-label">Ingestion</div>
        <div className="activity-list">
          {jobs.length ? jobs.map((job) => (
            <div className="activity-row" key={job.id}>
              <div>
                <strong>{words(job.state)}</strong>
                <span>Attempt {job.attemptCount} | {formatDate(job.updatedAt)}</span>
                {job.failureReason && <span className="failure">{job.failureReason}</span>}
              </div>
              {(job.state === "failed" || job.state === "waitingForOcr") && <button className="text-button" type="button" disabled={busy} onClick={() => run(() => portalApi.retryJob(job.id), "Job queued for retry.")}>Retry</button>}
            </div>
          )) : <p className="muted">No ingestion jobs.</p>}
        </div>
      </section>

      <section className="detail-section">
        <div className="section-label">Release</div>
        <p><span className={stateClass(dataset.visibility)}>{words(dataset.visibility)}</span></p>
        {dataset.visibility === "private" && <button className="secondary-button" type="button" disabled={busy} onClick={() => run(() => portalApi.submitForReview(dataset.id, dataset.concurrencyToken), "Submitted for review.")}>Submit for review</button>}
        {dataset.visibility === "pendingReview" && <button className="secondary-button" type="button" disabled={busy} onClick={() => run(() => portalApi.returnToPrivate(dataset.id, dataset.concurrencyToken), "Returned to private.")}>Return to private</button>}
        {isAdmin && (
          <>
            <label>Owner subject<input value={owner} maxLength="256" onChange={(event) => setOwner(event.target.value)} /></label>
            <button className="secondary-button" type="button" disabled={busy || !owner.trim()} onClick={() => run(() => portalApi.assignOwner(dataset.id, owner, dataset.concurrencyToken), "Owner updated.")}>Assign owner</button>
            <div className="release-list">
              {releases.map((release) => (
                <div className="release-row" key={release.id}>
                  <div><strong>{release.releaseKey}</strong><span>{words(release.state)} | {formatDate(release.updatedAt)}</span></div>
                  {release.state === "validated" && dataset.visibility === "pendingReview" && <button className="text-button" type="button" disabled={busy} onClick={() => run(() => portalApi.publish(dataset.id, release.id, dataset.concurrencyToken), "Release published.")}>Publish</button>}
                </div>
              ))}
            </div>
            <div className="danger-actions">
              {dataset.state === "archived"
                ? <button className="text-button" type="button" disabled={busy} onClick={() => run(() => portalApi.restore(dataset.id, dataset.concurrencyToken), "Dataset restored.")}>Restore dataset</button>
                : <button className="text-button danger" type="button" disabled={busy} onClick={() => run(() => portalApi.archive(dataset.id, dataset.concurrencyToken), "Dataset archived.")}>Archive dataset</button>}
            </div>
          </>
        )}
      </section>
    </aside>
  );
}

export default function KnowledgePortalPage() {
  const [session, setSession] = useState(null);
  const [datasets, setDatasets] = useState([]);
  const [adminDatasets, setAdminDatasets] = useState([]);
  const [selected, setSelected] = useState(null);
  const [sources, setSources] = useState([]);
  const [jobs, setJobs] = useState([]);
  const [releases, setReleases] = useState([]);
  const [view, setView] = useState("mine");
  const [newName, setNewName] = useState("");
  const [newDescription, setNewDescription] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const isAdmin = useMemo(() => session?.roles?.some((role) => role === "KnowledgeAdmin" || role === "Admin"), [session]);
  const displayed = view === "catalog" && isAdmin ? adminDatasets : datasets;

  const loadDetail = useCallback(async (dataset) => {
    setSelected(dataset);
    setSources([]);
    setJobs([]);
    setReleases([]);
    try {
      const values = await Promise.all([
        portalApi.listSources(dataset.id),
        portalApi.listJobs(dataset.id),
        isAdmin ? portalApi.listReleases(dataset.id) : Promise.resolve([]),
      ]);
      setSources(values[0]);
      setJobs(values[1]);
      setReleases(values[2]);
    } catch (requestError) {
      setError(requestError.message || "Unable to load dataset details.");
    }
  }, [isAdmin]);

  const refresh = useCallback(async (selectedId) => {
    setLoading(true);
    setError(null);
    try {
      const currentSession = await portalApi.session();
      setSession(currentSession);
      const admin = currentSession.roles.some((role) => role === "KnowledgeAdmin" || role === "Admin");
      const values = await Promise.all([portalApi.listOwned(), admin ? portalApi.listAdmin() : Promise.resolve([])]);
      setDatasets(values[0]);
      setAdminDatasets(values[1]);
      const current = selectedId ? values[0].concat(values[1]).find((item) => item.id === selectedId) : null;
      if (current) await loadDetail(current);
      else if (!selectedId && values[0][0]) await loadDetail(values[0][0]);
      else if (selectedId) setSelected(null);
    } catch (requestError) {
      if (requestError instanceof PortalApiError && requestError.status === 401) {
        window.location.assign("/auth/login?returnUrl=/");
        return;
      }
      setError(requestError.message || "Unable to load the portal.");
    } finally {
      setLoading(false);
    }
  }, [loadDetail]);
  useEffect(() => {
    void refresh();
  }, [refresh]);

  async function createDataset(event) {
    event.preventDefault();
    if (!newName.trim()) return;
    try {
      const dataset = await portalApi.createDataset({ name: newName, description: newDescription || null });
      setNewName("");
      setNewDescription("");
      await refresh(dataset.id);
    } catch (requestError) {
      setError(requestError.message || "Unable to create the dataset.");
    }
  }

  async function logout() {
    try { await portalApi.logout(); } finally { window.location.assign("/auth/login"); }
  }

  return (
    <main className="portal-shell">
      <header className="topbar">
        <div className="brand"><span className="brand-mark">K</span><div><strong>Knowledge Portal</strong><span>SperoFlow administration</span></div></div>
        <div className="user-tools"><span className="subject">{session?.name || session?.subject}</span><button className="text-button inverse" type="button" onClick={logout}>Sign out</button></div>
      </header>
      <div className="workspace">
        <section className="catalog-pane">
          <div className="catalog-head"><div><span className="eyebrow">Knowledge catalog</span><h1>Datasets</h1></div><button className="icon-button light" type="button" title="Refresh catalog" onClick={() => void refresh(selected?.id)} disabled={loading}>Refresh</button></div>
          {isAdmin && <div className="tabs" role="tablist" aria-label="Dataset view"><button className={view === "mine" ? "tab active" : "tab"} type="button" onClick={() => setView("mine")}>My access</button><button className={view === "catalog" ? "tab active" : "tab"} type="button" onClick={() => setView("catalog")}>Admin catalog</button></div>}
          <form className="create-form" onSubmit={createDataset}>
            <input value={newName} maxLength="240" onChange={(event) => setNewName(event.target.value)} placeholder="New dataset name" aria-label="New dataset name" />
            <input value={newDescription} maxLength="8000" onChange={(event) => setNewDescription(event.target.value)} placeholder="Description" aria-label="Dataset description" />
            <button className="primary-button" type="submit" disabled={loading || !newName.trim()}>Create</button>
          </form>
          {error && <p className="notice error">{error}</p>}
          {loading ? <div className="loading-line">Loading catalog</div> : <DatasetTable datasets={displayed} selectedId={selected?.id} onSelect={loadDetail} admin={view === "catalog" && isAdmin} />}
        </section>
        <DetailPanel dataset={selected} sources={sources} jobs={jobs} releases={releases} isAdmin={isAdmin} onRefresh={() => selected && loadDetail(selected)} onMutate={() => refresh(selected?.id)} />
      </div>
    </main>
  );
}
