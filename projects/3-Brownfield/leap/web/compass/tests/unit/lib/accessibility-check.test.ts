import { describe, expect, it, afterEach, vi } from 'vitest';
import { checkAccessibility } from '../../../src/lib/accessibility-check';

function withContainer(html: string): HTMLElement {
  const container = document.createElement('div');
  container.innerHTML = html;
  document.body.appendChild(container);
  return container;
}

describe('checkAccessibility', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('reports zero violations for a structurally clean, uncoloured container', async () => {
    const container = withContainer(
      '<nav aria-label="Compass navigation"><a href="/x">Team Directory</a></nav>',
    );
    const result = await checkAccessibility(container);
    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('catches a structural axe violation — an image with no alt text', async () => {
    const container = withContainer('<img src="x.png" />');
    const result = await checkAccessibility(container);
    expect(result.axeViolations.length).toBeGreaterThan(0);
    expect(result.axeViolations.some((v) => v.id === 'image-alt')).toBe(true);
  });

  it('does not run the color-contrast axe rule, which is unreliable under jsdom', async () => {
    // jsdom reports every element as zero-sized, so axe's own color-contrast rule would mark
    // this as "incomplete" rather than a reliable violation either way — it's disabled and the
    // dedicated walk below is the enforcement mechanism instead.
    const container = withContainer('<p style="color: rgb(152, 201, 61);">green text</p>');
    const result = await checkAccessibility(container);
    expect(result.axeViolations.some((v) => v.id === 'color-contrast')).toBe(false);
  });

  it('catches a brand-green-on-white contrast violation via the dedicated walk', async () => {
    const container = withContainer(
      '<p style="color: rgb(152, 201, 61); background-color: rgb(255, 255, 255);">green text</p>',
    );
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toHaveLength(1);
    expect(result.contrastViolations[0]).toContain('green text');
  });

  it('resolves the background from an ancestor when the text element sets none itself', async () => {
    const container = withContainer(
      '<div style="background-color: rgb(255, 255, 255);"><p style="color: rgb(152, 201, 61);">green text</p></div>',
    );
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toHaveLength(1);
  });

  it('falls back to the page default background when no ancestor sets one', async () => {
    const container = withContainer('<p style="color: rgb(152, 201, 61);">green text</p>');
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toHaveLength(1);
  });

  it('does not report a violation for text with no inline colour set (nothing to judge)', async () => {
    const container = withContainer('<p>ordinary text</p>');
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toEqual([]);
  });

  it('skips an element whose computed colour cannot be parsed as rgb(...)', async () => {
    // jsdom always resolves `color` to something parseable in practice (it implements the CSS
    // initial value). This forces the otherwise-unreachable "nothing to judge" path for real,
    // rather than leaving it as an untested defensive branch.
    const container = withContainer('<p>text</p>');
    const original = window.getComputedStyle;
    vi.spyOn(window, 'getComputedStyle').mockImplementation((element, pseudoElt) => {
      const real = original(element, pseudoElt);
      return new Proxy(real, {
        get(target, prop, receiver) {
          return prop === 'color' ? 'not-a-colour' : Reflect.get(target, prop, receiver);
        },
      });
    });

    const result = await checkAccessibility(container);

    expect(result.contrastViolations).toEqual([]);
    vi.restoreAllMocks();
  });

  it('does not report a violation for a passing colour pairing', async () => {
    const container = withContainer(
      '<p style="color: rgb(76, 77, 79); background-color: rgb(255, 255, 255);">gray text</p>',
    );
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toEqual([]);
  });

  it('does not double-count an inherited colour on both the container and its text-owning child', async () => {
    // The outer <div> "owns" no direct text node of its own — only its <span> child does. `color`
    // is CSS-inherited, so the violation surfaces once, from the element that actually owns the
    // visible text, not twice.
    const container = withContainer(
      '<div style="color: rgb(152, 201, 61);"><span>green text</span></div>',
    );
    const result = await checkAccessibility(container);
    expect(result.contrastViolations).toHaveLength(1);
  });
});
