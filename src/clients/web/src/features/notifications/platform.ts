export function isIosUserAgent(userAgent: string): boolean {
  return /iPad|iPhone|iPod/i.test(userAgent);
}

/** iOS shows install instructions until the PWA is launched from the home screen. */
export function shouldShowIosInstallInstructions(userAgent: string, standalone: boolean): boolean {
  return isIosUserAgent(userAgent) && standalone !== true;
}

export function readStandalone(nav: { standalone?: boolean }): boolean {
  return nav.standalone === true;
}
