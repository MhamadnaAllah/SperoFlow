/**
 * Format a Date object to a short readable string.
 * @param {Date} date
 * @returns {string} e.g. "May 10, 2026"
 */
export function formatDate(date) {
  return new Intl.DateTimeFormat('en-US', { month: 'long', day: 'numeric', year: 'numeric' }).format(date);
}

/**
 * Format minutes-since-midnight to "HH:MM AM/PM".
 * @param {number} totalMinutes
 * @returns {string}
 */
export function formatMinutes(totalMinutes) {
  const h = Math.floor(totalMinutes / 60) % 24;
  const min = totalMinutes % 60;
  const ampm = h < 12 ? 'AM' : 'PM';
  const hh = h % 12 || 12;
  return `${hh}:${min.toString().padStart(2, '0')} ${ampm}`;
}

/**
 * Format a time range from two minute values.
 * @param {number} startMin
 * @param {number} endMin
 * @returns {string}
 */
export function formatTimeRange(startMin, endMin) {
  return `${formatMinutes(startMin)} – ${formatMinutes(endMin)}`;
}
