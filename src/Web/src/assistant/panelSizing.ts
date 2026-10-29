/** Product rule: AI pane is sized as a share of the browser viewport. */
export const PANEL_MIN_VIEWPORT_RATIO = 0.3;
export const PANEL_DEFAULT_VIEWPORT_RATIO = 0.33;
export const PANEL_MAX_VIEWPORT_RATIO = 0.4;

/**
 * Minimum remaining width (px) for the AccountGo host column while the desktop
 * split pane is open. Tables use overflow-x and Dashboard's densest table is
 * min-w-[540px]; 600px keeps desktop chrome usable without force-mobile mode.
 */
export const HOST_MIN_PX = 600;

/** AccountGo sidebar widths (Tailwind w-20 / w-64). */
export const SIDEBAR_COLLAPSED_PX = 80;
export const SIDEBAR_EXPANDED_PX = 256;

export type AssistantPanelLayout = {
  /** Percentage of the SDK layout root (content column), for ChatSidepanelLayout. */
  minWidth: number;
  defaultWidth: number;
  maxWidth: number;
  /**
   * True when even the 30% viewport minimum cannot leave HOST_MIN_PX for the host.
   * Callers may enable compact/force-mobile host styling; P0 sizing still clamps.
   */
  compactHost: boolean;
};

function pxToLayoutPercent(panelPx: number, contentWidthPx: number): number {
  const safeContent = Math.max(1, contentWidthPx);
  return Math.round((panelPx / safeContent) * 10_000) / 100;
}

/**
 * Convert viewport-relative panel bounds into ChatSidepanelLayout percentages
 * (which are relative to the content column after the persistent sidebar).
 */
export function resolveAssistantPanelLayout(
  viewportWidthPx: number,
  sidebarWidthPx: number,
): AssistantPanelLayout {
  const viewport = Math.max(1, viewportWidthPx);
  const content = Math.max(1, viewport - Math.max(0, sidebarWidthPx));

  const minPx = PANEL_MIN_VIEWPORT_RATIO * viewport;
  const defaultPx = PANEL_DEFAULT_VIEWPORT_RATIO * viewport;
  const maxDesiredPx = PANEL_MAX_VIEWPORT_RATIO * viewport;
  const maxByHostPx = Math.max(0, content - HOST_MIN_PX);
  const maxPx = Math.min(maxDesiredPx, maxByHostPx);

  if (maxPx < minPx) {
    // Contingency: prefer keeping a usable chat over the host floor.
    const fallbackMaxPx = Math.min(maxDesiredPx, content * 0.45);
    const fallbackMinPx = Math.min(minPx, fallbackMaxPx);
    const fallbackDefaultPx = Math.min(Math.max(defaultPx, fallbackMinPx), fallbackMaxPx);
    return {
      minWidth: pxToLayoutPercent(fallbackMinPx, content),
      defaultWidth: pxToLayoutPercent(fallbackDefaultPx, content),
      maxWidth: pxToLayoutPercent(fallbackMaxPx, content),
      compactHost: true,
    };
  }

  const clampedDefaultPx = Math.min(Math.max(defaultPx, minPx), maxPx);
  return {
    minWidth: pxToLayoutPercent(minPx, content),
    defaultWidth: pxToLayoutPercent(clampedDefaultPx, content),
    maxWidth: pxToLayoutPercent(maxPx, content),
    compactHost: false,
  };
}
