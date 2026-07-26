import { apiRequest } from "./client";

/**
 * Lists only datasets the authenticated caller may retrieve from the isolated
 * knowledge platform. The main API proxies this catalog; the browser never
 * contacts the knowledge API or graph directly.
 */
export const ownedKnowledgeDatasetsApi = {
  list() {
    return apiRequest("/knowledge-datasets");
  },
};
