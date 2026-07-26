/**
 * Check if a value is non-empty.
 * @param {string} value
 * @returns {boolean}
 */
export function isRequired(value) {
  return value !== null && value !== undefined && String(value).trim().length > 0;
}

/**
 * Validate an email address.
 * @param {string} email
 * @returns {boolean}
 */
export function isValidEmail(email) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}

/**
 * Validate a goal/task title (non-empty, max 120 chars).
 * @param {string} title
 * @returns {{ valid: boolean, message?: string }}
 */
export function validateTitle(title) {
  if (!isRequired(title)) return { valid: false, message: 'Title is required.' };
  if (title.length > 120) return { valid: false, message: 'Title must be 120 characters or less.' };
  return { valid: true };
}
