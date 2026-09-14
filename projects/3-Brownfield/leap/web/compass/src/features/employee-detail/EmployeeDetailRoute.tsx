import { useParams } from '@tanstack/react-router';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { EmployeeDetailPage } from './EmployeeDetailPage';
import { useEmployeeDetail } from './useEmployeeDetail';

/**
 * Connects the employee detail screen to the read surface.
 *
 * Reads its own route parameter rather than taking it as a prop, so `routes/router.ts` needs no JSX
 * wrapper — which keeps that file free of components and therefore free of
 * `react-refresh/only-export-components`.
 */
export function EmployeeDetailRoute() {
  const { employeeId } = useParams({ from: '/team-directory/$employeeId' });
  const { data, isPending, isError } = useEmployeeDetail(Number(employeeId));
  const { data: user } = useCurrentUser();

  return (
    <EmployeeDetailPage
      detail={data}
      isPending={isPending}
      isError={isError}
      canManageAssignments={canManageCompassAssignments(getCompassPrivileges(user?.privileges))}
    />
  );
}
