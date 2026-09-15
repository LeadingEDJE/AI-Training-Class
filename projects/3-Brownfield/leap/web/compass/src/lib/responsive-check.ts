export interface OverflowViolation {
  element: string;
  declaredWidthPx: number;
  viewportPx: number;
}

const PX_WIDTH_STYLES = ['width', 'minWidth'] as const;

export function checkResponsiveOverflow(
  root: HTMLElement,
  viewportWidth: number,
): OverflowViolation[] {
  const violations: OverflowViolation[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT);
  let node = walker.nextNode() as HTMLElement | null;

  while (node) {
    for (const styleProp of PX_WIDTH_STYLES) {
      const declaredPx = parsePx(node.style[styleProp]);
      if (declaredPx !== null && declaredPx > viewportWidth) {
        violations.push({
          element: describeElement(node),
          declaredWidthPx: declaredPx,
          viewportPx: viewportWidth,
        });
      }
    }
    node = walker.nextNode() as HTMLElement | null;
  }

  return violations;
}

function parsePx(value: string): number | null {
  const match = /^(\d+(?:\.\d+)?)px$/.exec(value);
  return match ? parseFloat(match[1]) : null;
}

function describeElement(element: HTMLElement): string {
  const firstClass = element.className.split(/\s+/).filter(Boolean)[0];
  return firstClass
    ? `${element.tagName.toLowerCase()}.${firstClass}`
    : element.tagName.toLowerCase();
}
