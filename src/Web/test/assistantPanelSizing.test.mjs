import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  HOST_MIN_PX,
  PANEL_DEFAULT_VIEWPORT_RATIO,
  PANEL_MAX_VIEWPORT_RATIO,
  PANEL_MIN_VIEWPORT_RATIO,
  SIDEBAR_COLLAPSED_PX,
  resolveAssistantPanelLayout,
} from '../src/assistant/panelSizing.ts';

test('viewport ratios stay within 30–40% of the viewport after layout conversion', () => {
  const viewport = 1440;
  const layout = resolveAssistantPanelLayout(viewport, SIDEBAR_COLLAPSED_PX);
  const content = viewport - SIDEBAR_COLLAPSED_PX;

  const minPx = (layout.minWidth / 100) * content;
  const maxPx = (layout.maxWidth / 100) * content;
  const defaultPx = (layout.defaultWidth / 100) * content;

  assert.ok(Math.abs(minPx - PANEL_MIN_VIEWPORT_RATIO * viewport) < 1);
  assert.ok(maxPx <= PANEL_MAX_VIEWPORT_RATIO * viewport + 1);
  assert.ok(defaultPx >= minPx - 1);
  assert.ok(defaultPx <= maxPx + 1);
  assert.equal(layout.compactHost, false);
});

test('host floor clamps max panel near the SDK overlay edge', () => {
  const viewport = 1025;
  const layout = resolveAssistantPanelLayout(viewport, SIDEBAR_COLLAPSED_PX);
  const content = viewport - SIDEBAR_COLLAPSED_PX;
  const maxPx = (layout.maxWidth / 100) * content;
  const hostPx = content - maxPx;

  assert.ok(hostPx + 0.5 >= HOST_MIN_PX);
  assert.ok(maxPx <= PANEL_MAX_VIEWPORT_RATIO * viewport + 1);
  assert.equal(layout.compactHost, false);
});

test('full 40% viewport max is available once the host floor is satisfied', () => {
  const viewport = 1280;
  const layout = resolveAssistantPanelLayout(viewport, SIDEBAR_COLLAPSED_PX);
  const content = viewport - SIDEBAR_COLLAPSED_PX;
  const maxPx = (layout.maxWidth / 100) * content;

  assert.ok(Math.abs(maxPx - PANEL_MAX_VIEWPORT_RATIO * viewport) < 1);
  assert.ok(Math.abs((layout.defaultWidth / 100) * content - PANEL_DEFAULT_VIEWPORT_RATIO * viewport) < 1);
  assert.equal(layout.compactHost, false);
});

test('compactHost is true only when min panel cannot leave HOST_MIN for the host', () => {
  // Artificial narrow content: 30% of a huge viewport cannot fit with HOST_MIN.
  const layout = resolveAssistantPanelLayout(2000, 2000 - 500);
  assert.equal(layout.compactHost, true);
  assert.ok(layout.maxWidth >= layout.minWidth);
});
