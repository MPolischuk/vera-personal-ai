/** The hub ack is sent only while the tab is the visible document. */
export function shouldAckNotification(visibilityState: string): boolean {
  return visibilityState === "visible";
}
