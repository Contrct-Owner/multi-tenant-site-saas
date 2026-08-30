/** Pure hour-rendering logic, testable without a browser or a backend. */

const timeInZone = (utc: string, zone: string, hour12: boolean) =>
  new Date(utc).toLocaleTimeString(hour12 ? [] : 'en-GB', {
    hour: hour12 ? 'numeric' : '2-digit',
    minute: '2-digit',
    timeZone: zone,
    hour12,
  });

/** "9:00 AM – 5:00 PM" in the SITE's zone - or "Open 24 hours" when it is. */
export function spanLabel(startUtc: string, endUtc: string, zone: string): string {
  const start24 = timeInZone(startUtc, zone, false);
  const end24 = timeInZone(endUtc, zone, false);
  if (start24 === '00:00' && end24 === '23:59') return 'Open 24 hours';
  return `${timeInZone(startUtc, zone, true)} – ${timeInZone(endUtc, zone, true)}`;
}

/** Is this site-local calendar date "today" on the site's own clock? */
export function isTodayInZone(localDate: string, zone: string, now: number): boolean {
  // en-CA formats as YYYY-MM-DD, matching the API's localDate
  return new Date(now).toLocaleDateString('en-CA', { timeZone: zone }) === localDate;
}
