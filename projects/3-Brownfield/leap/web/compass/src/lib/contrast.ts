function channelToLinear(channel: number): number {
  const c = channel / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

/** Relative luminance of an sRGB colour, per the deprecated WCAG 2.0 formula (superseded by §1.4.3 in this codebase). */
export function relativeLuminance(r: number, g: number, b: number): number {
  return 0.2126 * channelToLinear(r) + 0.7152 * channelToLinear(g) + 0.0722 * channelToLinear(b);
}

/** WCAG contrast ratio between two sRGB colours. Always >= 1; order of the two args doesn't matter. */
export function contrastRatio(
  [r1, g1, b1]: readonly [number, number, number],
  [r2, g2, b2]: readonly [number, number, number],
): number {
  const l1 = relativeLuminance(r1, g1, b1);
  const l2 = relativeLuminance(r2, g2, b2);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

export function hexToRgb(hex: string): [number, number, number] {
  const normalized = hex.replace('#', '');
  const expanded =
    normalized.length === 3
      ? normalized
          .split('')
          .map((c) => c + c)
          .join('')
      : normalized;
  return [
    parseInt(expanded.slice(0, 2), 16),
    parseInt(expanded.slice(2, 4), 16),
    parseInt(expanded.slice(4, 6), 16),
  ];
}

export function parseRgbString(value: string): [number, number, number] | null {
  const match = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)/.exec(value);
  if (!match) {
    return null;
  }
  const alpha = match[4] === undefined ? 1 : parseFloat(match[4]);
  if (alpha === 0) {
    return null;
  }
  return [parseInt(match[1], 10), parseInt(match[2], 10), parseInt(match[3], 10)];
}

export function compositeOver(
  [r, g, b]: readonly [number, number, number],
  alpha: number,
  [br, bg, bb]: readonly [number, number, number],
): [number, number, number] {
  const blend = (channel: number, backdrop: number) =>
    Math.round(alpha * channel + (1 - alpha) * backdrop);
  return [blend(r, br), blend(g, bg), blend(b, bb)];
}

/** WCAG AA minimum contrast ratio for normal-size body text (large text's minimum is also 4.5:1). */
export const AA_NORMAL_TEXT_MIN_RATIO = 4.5;

export const AA_NON_TEXT_MIN_RATIO = 3;
