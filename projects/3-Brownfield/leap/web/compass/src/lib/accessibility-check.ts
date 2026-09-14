import axe from 'axe-core';
import { AA_NORMAL_TEXT_MIN_RATIO, contrastRatio, parseRgbString } from './contrast';

export interface AccessibilityCheckResult {
  axeViolations: axe.Result[];
  contrastViolations: string[];
}

/**
 * The Compass US7 WCAG 2.1 AA baseline (#57): axe-core's structural ruleset (roles, labels,
 * landmarks — reliable under jsdom) plus a dedicated inline-colour contrast walk. axe-core's own
 * `color-contrast` rule is disabled: it needs real layout/paint to decide whether an element is
 * visible, and jsdom reports every element as zero-sized, so the rule can only ever report
 * "incomplete", never a reliable pass or fail. The walk below reads `getComputedStyle` directly
 * instead, which jsdom resolves correctly for colours set via inline style — that's the
 * enforcement mechanism `tests/unit/lib/accessibility-check.test.ts` proves can fail.
 *
 * The real `color-contrast` rule — the thing this file cannot check, since almost every colour
 * here comes from a Tailwind class, not an inline style — runs instead in a real browser via
 * `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts` (`@axe-core/playwright`
 * against the live page). Confirmed zero violations there as of 2026-08-10.
 */
export async function checkAccessibility(
  container: HTMLElement,
): Promise<AccessibilityCheckResult> {
  const axeResults = await axe.run(container, {
    rules: { 'color-contrast': { enabled: false } },
  });

  return {
    axeViolations: axeResults.violations,
    contrastViolations: findContrastViolations(container),
  };
}

/** The page's default background (`html { background-color: #f4f5f6 }` in index.css), used when no ancestor declares one. */
const PAGE_DEFAULT_BACKGROUND: [number, number, number] = [244, 245, 246];

function findContrastViolations(root: HTMLElement): string[] {
  const violations: string[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT);
  let node = walker.nextNode() as HTMLElement | null;

  while (node) {
    if (ownsDirectText(node)) {
      const text = (node.textContent ?? '').trim();
      const style = getComputedStyle(node);
      const foreground = parseRgbString(style.color);
      const background = resolveBackground(node);

      if (foreground) {
        // `background` always resolves — `resolveBackground` falls back to the page default —
        // so only `foreground` (an unparseable computed colour) gates whether there's anything
        // to judge.
        const ratio = contrastRatio(foreground, background);
        if (ratio < AA_NORMAL_TEXT_MIN_RATIO) {
          const [bgR, bgG, bgB] = background;
          violations.push(
            `"${text}" (${style.color} on rgb(${bgR}, ${bgG}, ${bgB})) ` +
              `measures ${ratio.toFixed(2)}:1, below the ${AA_NORMAL_TEXT_MIN_RATIO}:1 AA minimum`,
          );
        }
      }
    }
    node = walker.nextNode() as HTMLElement | null;
  }

  return violations;
}

/** True when the element has a non-blank text node directly among its children (not only via nested elements). */
function ownsDirectText(element: HTMLElement): boolean {
  return Array.from(element.childNodes).some(
    (child) => child.nodeType === Node.TEXT_NODE && Boolean(child.textContent?.trim()),
  );
}

function resolveBackground(node: HTMLElement): [number, number, number] {
  let current: HTMLElement | null = node;
  while (current) {
    const background = parseRgbString(getComputedStyle(current).backgroundColor);
    if (background) {
      return background;
    }
    current = current.parentElement;
  }
  return PAGE_DEFAULT_BACKGROUND;
}
