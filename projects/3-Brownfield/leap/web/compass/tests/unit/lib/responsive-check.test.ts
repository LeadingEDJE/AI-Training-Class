import { describe, expect, it, afterEach } from 'vitest';
import { checkResponsiveOverflow } from '../../../src/lib/responsive-check';

function withContainer(html: string): HTMLElement {
  const container = document.createElement('div');
  container.innerHTML = html;
  document.body.appendChild(container);
  return container;
}

describe('checkResponsiveOverflow', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('reports no violations for a container with no fixed-width elements', () => {
    const container = withContainer('<p class="max-w-2xl">Hello</p>');
    expect(checkResponsiveOverflow(container, 390)).toEqual([]);
  });

  it('flags an element with an inline width wider than the viewport', () => {
    const container = withContainer('<div style="width: 500px;">too wide</div>');
    const violations = checkResponsiveOverflow(container, 390);
    expect(violations).toHaveLength(1);
    expect(violations[0]).toMatchObject({ declaredWidthPx: 500, viewportPx: 390 });
  });

  it('does not flag an inline width that fits the viewport', () => {
    const container = withContainer('<div style="width: 200px;">fits</div>');
    expect(checkResponsiveOverflow(container, 390)).toEqual([]);
  });

  it('flags an element with an inline min-width wider than the viewport', () => {
    const container = withContainer('<div style="min-width: 1024px;">too wide</div>');
    const violations = checkResponsiveOverflow(container, 767);
    expect(violations).toHaveLength(1);
    expect(violations[0].declaredWidthPx).toBe(1024);
  });

  it('ignores non-pixel width values such as percentages or auto', () => {
    const container = withContainer(
      '<div style="width: 100%;"><div style="width: auto;">nested</div></div>',
    );
    expect(checkResponsiveOverflow(container, 390)).toEqual([]);
  });

  it('describes the offending element by tag name and first class', () => {
    const container = withContainer('<div class="banner wide" style="width: 2000px;"></div>');
    const [violation] = checkResponsiveOverflow(container, 390);
    expect(violation.element).toBe('div.banner');
  });

  it('describes an offending element with no class by tag name alone', () => {
    const container = withContainer('<span style="width: 2000px;"></span>');
    const [violation] = checkResponsiveOverflow(container, 390);
    expect(violation.element).toBe('span');
  });

  it('walks nested elements, not just direct children', () => {
    const container = withContainer(
      '<section><article><div style="width: 900px;"></div></article></section>',
    );
    const violations = checkResponsiveOverflow(container, 390);
    expect(violations).toHaveLength(1);
  });
});
