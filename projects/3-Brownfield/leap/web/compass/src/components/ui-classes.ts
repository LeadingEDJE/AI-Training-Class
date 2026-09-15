export const CONTROL_BORDER = 'border-brand-gray/70';

export const fieldControlClass = `w-full rounded border ${CONTROL_BORDER} bg-white px-2 py-1.5 text-sm text-brand-text`;

/** The accent fill for a checked/active control. No border needed — brand green alone clears the WCAG boundary requirement. */
export const ACCENT_SURFACE = 'border-brand-green-700 bg-brand-green';

/** The single definition of a button's appearance, per the sizing table in `web/compass/README.md#buttons`. */
export function buttonClassName({
  variant,
  size = 'default',
}: {
  variant: 'primary' | 'secondary';
  size?: 'default' | 'small';
}): string {
  const padding = size === 'small' ? 'px-3 py-1 text-xs' : 'px-3 py-1.5 text-sm';

  const tone =
    variant === 'primary'
      ? `${ACCENT_SURFACE} text-brand-ink hover:bg-brand-green-700`
      : `${CONTROL_BORDER} bg-white text-brand-gray hover:bg-brand-green/10`;

  return `rounded border font-semibold disabled:cursor-not-allowed disabled:opacity-60 ${padding} ${tone}`;
}

/** The single definition of a link in a table cell. Weight, wrapping and size belong to the cell. */
export const tableLinkClass =
  'text-brand-text underline decoration-brand-green-700 decoration-2 underline-offset-2 hover:text-brand-green-accent';
