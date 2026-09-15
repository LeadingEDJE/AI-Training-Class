import { useParams } from '@tanstack/react-router';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { EmployeeDetailPage } from './EmployeeDetailPage';
import { useEmployeeDetail } from './useEmployeeDetail';

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
