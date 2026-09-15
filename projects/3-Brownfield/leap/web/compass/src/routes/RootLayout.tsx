import { Outlet } from '@tanstack/react-router';
import { CompassNav } from '../components/CompassNav';

/** The shell every Compass route renders inside: `CompassNav` wraps the matched route's content. */
export function RootLayout() {
  return (
    <>
      <CompassNav />
      <Outlet />
    </>
  );
}
