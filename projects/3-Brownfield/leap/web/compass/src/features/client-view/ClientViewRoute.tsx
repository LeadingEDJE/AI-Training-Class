import { useParams } from '@tanstack/react-router';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { ClientViewPage } from './ClientViewPage';
import { useClientView } from './useClientView';

/** Connects the client view to the read surface, reading its own route parameter. */
export function ClientViewRoute() {
  const { clientId } = useParams({ from: '/client-directory/$clientId' });
  const { data, isPending, isError } = useClientView(Number(clientId));
  const { data: user } = useCurrentUser();

  return (
    <ClientViewPage
      view={data}
      isPending={isPending}
      isError={isError}
      canManageAssignments={canManageCompassAssignments(getCompassPrivileges(user?.privileges))}
    />
  );
}
