import axe from 'axe-core';
import { AA_NORMAL_TEXT_MIN_RATIO, contrastRatio, parseRgbString } from './contrast';

export interface AccessibilityCheckResult {
  axeViolations: axe.Result[];
  contrastViolations: string[];
}

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

/** The page's default background, sourced from the `--color-shell-bg` token documented in RUNNING.md. */
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

/** True when the element has any text node among its descendants, per `dom-text-ownership.test.ts`. */
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
